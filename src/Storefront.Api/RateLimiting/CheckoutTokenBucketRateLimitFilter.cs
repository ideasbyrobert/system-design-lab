using Lab.Shared.Checkout;
using Lab.Shared.Configuration;
using Lab.Shared.Contracts;
using Lab.Shared.Http;
using Lab.Shared.RateLimiting;
using Lab.Telemetry.AspNetCore;
using Lab.Telemetry.RequestTracing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Storefront.Api.Checkout;
using Storefront.Api.ProductPages;
using Storefront.Api.Routing;

namespace Storefront.Api.RateLimiting;

internal sealed class CheckoutTokenBucketRateLimitFilter(
    ITokenBucketRateLimiter rateLimiter,
    IOptions<RateLimiterOptions> options,
    IRequestTraceContextAccessor traceAccessor) : IEndpointFilter
{
    private const string CheckoutPolicyName = "checkout";

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        StorefrontCheckoutRequest? request = context.Arguments.OfType<StorefrontCheckoutRequest>().FirstOrDefault();
        if (request is null)
        {
            return next(context);
        }

        TokenBucketRateLimitPolicy policy = CreateCheckoutPolicy(options.Value);
        if (!policy.Enabled)
        {
            return next(context);
        }

        HttpContext httpContext = context.HttpContext;
        RequestTraceContext trace = traceAccessor.Current
            ?? throw new InvalidOperationException("Request trace context is required before checkout rate limiting runs.");
        StorefrontSessionKeyResolution session = httpContext.GetRequiredStorefrontSessionKeyResolution();
        string? checkoutMode = NormalizeOptionalText(httpContext.Request.Query["mode"].ToString());
        SetOperationContract(trace, checkoutMode);
        string partitionKey = ResolvePartitionKey(request, session, httpContext);

        TokenBucketRateLimitDecision decision = rateLimiter.TryConsume(
            policy,
            httpContext.Request.Path.Value ?? "/checkout",
            partitionKey);

        trace.RecordInstantStage(
            "rate_limit_checked",
            outcome: decision.Allowed ? "allowed" : "rejected",
            metadata: new Dictionary<string, string?>
            {
                ["policy"] = decision.PolicyName,
                ["partitionKey"] = decision.PartitionKey,
                ["capacity"] = decision.Capacity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["tokensRemaining"] = decision.TokensRemaining.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                ["retryAfterSeconds"] = decision.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });

        if (decision.Allowed)
        {
            return next(context);
        }

        string? idempotencyKey = NormalizeOptionalText(httpContext.Request.Headers[LabHeaderNames.IdempotencyKey].ToString());
        trace.MarkRateLimited();
        trace.SetUserId(request.UserId);
        trace.SetErrorCode("rate_limited");
        trace.AddNote("Storefront rejected checkout before calling Order.Api because the custom token bucket was empty.");
        trace.RecordInstantStage(
            "response_sent",
            outcome: "rate_limited",
            metadata: new Dictionary<string, string?>
            {
                ["statusCode"] = StatusCodes.Status429TooManyRequests.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["retryAfterSeconds"] = decision.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["policy"] = decision.PolicyName,
                ["partitionKey"] = decision.PartitionKey,
                ["sessionKey"] = session.SessionKey
            });

        if (idempotencyKey is not null)
        {
            httpContext.Response.Headers[LabHeaderNames.IdempotencyKey] = idempotencyKey;
        }

        httpContext.Response.Headers["Retry-After"] = decision.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        StorefrontCheckoutFailureResponse body = new(
            Error: "rate_limited",
            Detail: $"Too many checkout attempts for '{decision.PartitionKey}'. Retry after {decision.RetryAfterSeconds} second(s).",
            ContractSatisfied: false,
            UserId: request.UserId,
            IdempotencyKey: idempotencyKey,
            PaymentMode: request.PaymentMode,
            CheckoutMode: checkoutMode,
            Source: "storefront",
            Order: null,
            Request: new StorefrontRequestInfo(
                RunId: trace.RunId,
                TraceId: trace.TraceId,
                RequestId: trace.RequestId,
                CorrelationId: trace.CorrelationId));

        return ValueTask.FromResult<object?>(TypedResults.Json(body, statusCode: StatusCodes.Status429TooManyRequests));
    }

    private static TokenBucketRateLimitPolicy CreateCheckoutPolicy(RateLimiterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new TokenBucketRateLimitPolicy(
            name: CheckoutPolicyName,
            capacity: options.Checkout.ResolveCapacity(options.TokenBucketCapacity),
            tokensPerSecond: options.Checkout.ResolveTokensPerSecond(options.TokensPerSecond),
            enabled: options.Checkout.Enabled);
    }

    private static string ResolvePartitionKey(
        StorefrontCheckoutRequest request,
        StorefrontSessionKeyResolution session,
        HttpContext httpContext)
    {
        string? userId = NormalizeOptionalText(request.UserId);
        if (userId is not null)
        {
            return $"user:{userId}";
        }

        if (!string.IsNullOrWhiteSpace(session.SessionKey))
        {
            return $"session:{session.SessionKey}";
        }

        string? correlationId = NormalizeOptionalText(httpContext.Request.Headers[LabHeaderNames.CorrelationId].ToString());
        if (correlationId is not null)
        {
            return $"correlation:{correlationId}";
        }

        string? remoteIp = NormalizeOptionalText(httpContext.Connection.RemoteIpAddress?.ToString());
        return remoteIp is null ? "client:anonymous" : $"client:{remoteIp}";
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void SetOperationContract(RequestTraceContext trace, string? checkoutMode)
    {
        if (CheckoutExecutionModes.TryParse(checkoutMode, out string normalizedMode))
        {
            trace.SetOperationContract(
                CheckoutExecutionModes.IsAsync(normalizedMode)
                    ? BusinessOperationContracts.StorefrontCheckoutAsync
                    : BusinessOperationContracts.StorefrontCheckoutSync);
            return;
        }

        trace.SetOperationContract(BusinessOperationContracts.StorefrontCheckout);
    }
}
