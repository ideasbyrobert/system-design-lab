using System.Net.Http.Json;
using Lab.Shared.Http;

namespace Order.Api.Checkout;

internal sealed class HttpOrderPaymentClient(HttpClient httpClient) : IOrderPaymentClient
{
    public async Task<HttpResponseMessage> AuthorizeAsync(
        OrderPaymentAuthorizationRequest request,
        string runId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        string path = string.IsNullOrWhiteSpace(request.PaymentMode)
            ? "/payments/authorize"
            : $"/payments/authorize?mode={Uri.EscapeDataString(request.PaymentMode.Trim())}";

        using HttpRequestMessage message = new(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new
            {
                request.PaymentId,
                request.OrderId,
                request.AmountCents,
                request.Currency,
                CallbackUrl = request.CallbackUrl
            })
        };

        message.Headers.TryAddWithoutValidation(LabHeaderNames.RunId, runId);
        message.Headers.TryAddWithoutValidation(LabHeaderNames.CorrelationId, correlationId);

        if (request.DebugTelemetryRequested)
        {
            message.Headers.TryAddWithoutValidation(LabHeaderNames.DebugTelemetry, "true");
        }

        return await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
