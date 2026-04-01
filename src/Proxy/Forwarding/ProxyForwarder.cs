using System.Diagnostics;
using Lab.Shared.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Proxy.Routing;

namespace Proxy.Forwarding;

public sealed class ProxyForwarder(
    IHttpClientFactory httpClientFactory,
    ProxyRoutingService routingService,
    ILogger<ProxyForwarder> logger)
{
    public const string BackendClientName = "proxy-backend";
    public const string ProxyBackendHeaderName = "X-Proxy-Backend";
    public const string ProxyRouteHeaderName = "X-Proxy-Route";
    public const string ProxyRoutingModeHeaderName = "X-Proxy-Routing-Mode";

    private static readonly HashSet<string> ForwardedRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Accept",
        "Authorization",
        "Cookie",
        "User-Agent",
        LabHeaderNames.RunId,
        LabHeaderNames.CorrelationId,
        LabHeaderNames.SessionKey,
        LabHeaderNames.IdempotencyKey,
        LabHeaderNames.DebugTelemetry,
        LabHeaderNames.PaymentSimulatorMode
    };

    private static readonly HashSet<string> ForwardedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Type",
        "Retry-After",
        "Set-Cookie",
        LabHeaderNames.RunId,
        LabHeaderNames.CorrelationId,
        LabHeaderNames.IdempotencyKey,
        LabHeaderNames.RequestId,
        LabHeaderNames.TraceId
    };

    public async Task ForwardAsync(
        HttpContext httpContext,
        ProxyRoutingDecision decision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(decision);

        byte[]? requestBody = await CaptureRequestBodyAsync(httpContext.Request, cancellationToken).ConfigureAwait(false);

        HttpClient client = httpClientFactory.CreateClient(BackendClientName);
        ProxyRoutingDecision currentDecision = decision;
        bool remapAttempted = false;

        while (true)
        {
            using HttpRequestMessage request = CreateProxyRequest(httpContext, currentDecision, requestBody);
            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                using HttpResponseMessage response = await client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                httpContext.Response.StatusCode = (int)response.StatusCode;
                CopyResponseHeaders(response, httpContext.Response.Headers);
                httpContext.Response.Headers[ProxyBackendHeaderName] = currentDecision.BackendBaseUri.AbsoluteUri;
                httpContext.Response.Headers[ProxyRouteHeaderName] = currentDecision.RouteName;
                httpContext.Response.Headers[ProxyRoutingModeHeaderName] = currentDecision.RoutingMode;

                await response.Content.CopyToAsync(httpContext.Response.Body, cancellationToken).ConfigureAwait(false);

                logger.LogInformation(
                    "Proxy routed {Method} {Path}{Query} using mode {RoutingMode} ({BackendSelection}) to {RouteName} backend {Backend} with status {StatusCode} in {ElapsedMs} ms. RunId={RunId} CorrelationId={CorrelationId} SessionKeySource={SessionKeySource}",
                    httpContext.Request.Method,
                    httpContext.Request.Path.Value ?? "/",
                    httpContext.Request.QueryString.Value ?? string.Empty,
                    currentDecision.RoutingMode,
                    currentDecision.BackendSelection,
                    currentDecision.RouteName,
                    currentDecision.BackendBaseUri.AbsoluteUri,
                    (int)response.StatusCode,
                    stopwatch.Elapsed.TotalMilliseconds,
                    GetHeaderValue(httpContext.Request.Headers, LabHeaderNames.RunId),
                    GetHeaderValue(httpContext.Request.Headers, LabHeaderNames.CorrelationId),
                    currentDecision.SessionKeySource);
                return;
            }
            catch (HttpRequestException exception)
            {
                string? remapError = null;

                if (!remapAttempted &&
                    routingService.TryRemapAfterTransportFailure(currentDecision, out ProxyRoutingDecision? remappedDecision, out remapError))
                {
                    remapAttempted = true;

                    logger.LogWarning(
                        exception,
                        "Proxy transport failure reached sticky assignment {Backend} on route {RouteName}. Remapping session to {RemappedBackend}.",
                        currentDecision.BackendBaseUri.AbsoluteUri,
                        currentDecision.RouteName,
                        remappedDecision!.BackendBaseUri.AbsoluteUri);

                    currentDecision = remappedDecision!;
                    continue;
                }

                logger.LogWarning(
                    exception,
                    "Proxy could not reach backend {Backend} for {Method} {Path}{Query}. RoutingMode={RoutingMode} BackendSelection={BackendSelection} RemapAttempted={RemapAttempted} RemapError={RemapError}",
                    currentDecision.BackendBaseUri.AbsoluteUri,
                    httpContext.Request.Method,
                    httpContext.Request.Path.Value ?? "/",
                    httpContext.Request.QueryString.Value ?? string.Empty,
                    currentDecision.RoutingMode,
                    currentDecision.BackendSelection,
                    remapAttempted,
                    remapAttempted ? null : remapError);

                httpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
                httpContext.Response.ContentType = "application/json";
                httpContext.Response.Headers[ProxyBackendHeaderName] = currentDecision.BackendBaseUri.AbsoluteUri;
                httpContext.Response.Headers[ProxyRouteHeaderName] = currentDecision.RouteName;
                httpContext.Response.Headers[ProxyRoutingModeHeaderName] = currentDecision.RoutingMode;

                await httpContext.Response.WriteAsJsonAsync(
                    new
                    {
                        error = "proxy_transport_error",
                        detail = $"Proxy could not reach backend '{currentDecision.BackendBaseUri.AbsoluteUri}'."
                    },
                    cancellationToken).ConfigureAwait(false);
                return;
            }
        }
    }

    private static async Task<byte[]?> CaptureRequestBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!RequestMayContainBody(request.Method))
        {
            return null;
        }

        request.EnableBuffering();

        using MemoryStream buffer = new();
        await request.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        request.Body.Position = 0;

        byte[] bytes = buffer.ToArray();
        return bytes.Length == 0 ? null : bytes;
    }

    private static HttpRequestMessage CreateProxyRequest(
        HttpContext httpContext,
        ProxyRoutingDecision decision,
        byte[]? requestBody)
    {
        HttpRequestMessage proxyRequest = new(new HttpMethod(httpContext.Request.Method), decision.ForwardUri);

        if (requestBody is not null)
        {
            ByteArrayContent content = new(requestBody);

            if (!string.IsNullOrWhiteSpace(httpContext.Request.ContentType))
            {
                content.Headers.TryAddWithoutValidation("Content-Type", httpContext.Request.ContentType);
            }

            proxyRequest.Content = content;
        }

        foreach ((string headerName, StringValues values) in httpContext.Request.Headers)
        {
            if (!ForwardedRequestHeaders.Contains(headerName))
            {
                continue;
            }

            if (!proxyRequest.Headers.TryAddWithoutValidation(headerName, values.ToArray()) &&
                proxyRequest.Content is not null)
            {
                proxyRequest.Content.Headers.TryAddWithoutValidation(headerName, values.ToArray());
            }
        }

        return proxyRequest;
    }

    private static void CopyResponseHeaders(HttpResponseMessage response, IHeaderDictionary headers)
    {
        foreach ((string headerName, IEnumerable<string> values) in response.Headers)
        {
            if (ForwardedResponseHeaders.Contains(headerName))
            {
                headers[headerName] = new StringValues(values.ToArray());
            }
        }

        foreach ((string headerName, IEnumerable<string> values) in response.Content.Headers)
        {
            if (ForwardedResponseHeaders.Contains(headerName))
            {
                headers[headerName] = new StringValues(values.ToArray());
            }
        }
    }

    private static bool RequestMayContainBody(string method) =>
        HttpMethods.IsPost(method) ||
        HttpMethods.IsPut(method) ||
        HttpMethods.IsPatch(method) ||
        HttpMethods.IsDelete(method);

    private static string? GetHeaderValue(IHeaderDictionary headers, string headerName) =>
        headers.TryGetValue(headerName, out StringValues value)
            ? value.ToString()
            : null;
}
