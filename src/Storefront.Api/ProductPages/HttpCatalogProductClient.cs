using Lab.Shared.Http;
using Lab.Shared.Networking;

namespace Storefront.Api.ProductPages;

internal sealed class HttpCatalogProductClient(HttpClient httpClient) : ICatalogProductClient
{
    public async Task<HttpResponseMessage> GetProductAsync(
        string productId,
        string runId,
        string correlationId,
        ProductReadSource readSource,
        bool debugTelemetryRequested,
        CatalogDependencyRoutePlan routePlan,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(routePlan);

        Uri routeBaseUri = new(routePlan.EffectiveBaseUrl, UriKind.Absolute);
        Uri requestUri = new(
            routeBaseUri,
            $"catalog/products/{Uri.EscapeDataString(productId)}?readSource={Uri.EscapeDataString(readSource.ToText())}");

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);

        request.Headers.TryAddWithoutValidation(LabHeaderNames.RunId, runId);
        request.Headers.TryAddWithoutValidation(LabHeaderNames.CorrelationId, correlationId);
        request.Options.Set(RegionLatencyRequestOptions.TargetRegion, routePlan.EffectiveTargetRegion);

        if (debugTelemetryRequested)
        {
            request.Headers.TryAddWithoutValidation(LabHeaderNames.DebugTelemetry, "true");
        }

        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
