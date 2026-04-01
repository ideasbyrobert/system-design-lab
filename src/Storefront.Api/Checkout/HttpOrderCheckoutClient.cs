using System.Net.Http.Json;
using Lab.Shared.Http;

namespace Storefront.Api.Checkout;

internal sealed class HttpOrderCheckoutClient(HttpClient httpClient) : IOrderCheckoutClient
{
    public async Task<HttpResponseMessage> CheckoutAsync(
        StorefrontCheckoutRequest request,
        string checkoutMode,
        string idempotencyKey,
        string runId,
        string correlationId,
        bool debugTelemetryRequested,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkoutMode);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        using HttpRequestMessage message = new(
            HttpMethod.Post,
            $"/orders/checkout?mode={Uri.EscapeDataString(checkoutMode)}")
        {
            Content = JsonContent.Create(request)
        };

        message.Headers.TryAddWithoutValidation(LabHeaderNames.RunId, runId);
        message.Headers.TryAddWithoutValidation(LabHeaderNames.CorrelationId, correlationId);
        message.Headers.TryAddWithoutValidation(LabHeaderNames.IdempotencyKey, idempotencyKey);

        if (debugTelemetryRequested)
        {
            message.Headers.TryAddWithoutValidation(LabHeaderNames.DebugTelemetry, "true");
        }

        return await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
