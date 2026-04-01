using System.Net.Http.Json;
using System.Text.Json;
using Lab.Shared.Http;

namespace Worker.Jobs;

internal interface IPaymentConfirmationClient
{
    Task<PaymentProviderObservation> AuthorizeAsync(
        PaymentAuthorizationCommand command,
        string runId,
        string correlationId,
        CancellationToken cancellationToken);

    Task<PaymentProviderObservation> GetStatusAsync(
        string paymentId,
        string runId,
        string correlationId,
        CancellationToken cancellationToken);
}

internal sealed record PaymentAuthorizationCommand(
    string PaymentId,
    string OrderId,
    int AmountCents,
    string Currency,
    string PaymentMode);

internal sealed record PaymentProviderObservation(
    int StatusCode,
    string Outcome,
    string? ErrorCode,
    string? ErrorDetail,
    string? ProviderReference,
    bool CallbackPending,
    int CallbackCountScheduled,
    string? DownstreamRunId,
    string? DownstreamTraceId,
    string? DownstreamRequestId);

internal sealed class HttpPaymentConfirmationClient(HttpClient httpClient) : IPaymentConfirmationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PaymentProviderObservation> AuthorizeAsync(
        PaymentAuthorizationCommand command,
        string runId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        string path = $"/payments/authorize?mode={Uri.EscapeDataString(command.PaymentMode)}";

        using HttpRequestMessage request = new(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new
            {
                command.PaymentId,
                command.OrderId,
                command.AmountCents,
                command.Currency
            })
        };

        request.Headers.TryAddWithoutValidation(LabHeaderNames.RunId, runId);
        request.Headers.TryAddWithoutValidation(LabHeaderNames.CorrelationId, correlationId);

        using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken);

        using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
        JsonElement root = document.RootElement;

        return new PaymentProviderObservation(
            StatusCode: (int)response.StatusCode,
            Outcome: ReadString(root, "outcome") ?? (response.IsSuccessStatusCode ? "unknown" : "failed"),
            ErrorCode: ReadString(root, "error"),
            ErrorDetail: ReadString(root, "detail"),
            ProviderReference: ReadString(root, "providerReference"),
            CallbackPending: root.TryGetProperty("callbackPending", out JsonElement callbackPending) && callbackPending.ValueKind == JsonValueKind.True,
            CallbackCountScheduled: ReadInt(root, "callbackCountScheduled") ?? 0,
            DownstreamRunId: ReadNestedString(root, "request", "runId"),
            DownstreamTraceId: ReadNestedString(root, "request", "traceId"),
            DownstreamRequestId: ReadNestedString(root, "request", "requestId"));
    }

    public async Task<PaymentProviderObservation> GetStatusAsync(
        string paymentId,
        string runId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, $"/payments/authorizations/{Uri.EscapeDataString(paymentId)}");
        request.Headers.TryAddWithoutValidation(LabHeaderNames.RunId, runId);
        request.Headers.TryAddWithoutValidation(LabHeaderNames.CorrelationId, correlationId);

        using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken);

        using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
        JsonElement root = document.RootElement;

        JsonElement callbacks = root.TryGetProperty("callbacks", out JsonElement callbacksValue)
            ? callbacksValue
            : default;

        int callbackCount = callbacks.ValueKind == JsonValueKind.Array ? callbacks.GetArrayLength() : 0;
        bool callbackPending = callbacks.ValueKind == JsonValueKind.Array &&
                               callbacks.EnumerateArray().Any(item =>
                                   string.Equals(ReadString(item, "status"), "Pending", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(ReadString(item, "status"), "Dispatching", StringComparison.OrdinalIgnoreCase));

        return new PaymentProviderObservation(
            StatusCode: (int)response.StatusCode,
            Outcome: ReadString(root, "outcome") ?? (response.IsSuccessStatusCode ? "unknown" : "failed"),
            ErrorCode: ReadString(root, "error"),
            ErrorDetail: ReadString(root, "detail"),
            ProviderReference: ReadString(root, "latestProviderReference"),
            CallbackPending: callbackPending,
            CallbackCountScheduled: callbackCount,
            DownstreamRunId: ReadNestedString(root, "request", "runId"),
            DownstreamTraceId: ReadNestedString(root, "request", "traceId"),
            DownstreamRequestId: ReadNestedString(root, "request", "requestId"));
    }

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out JsonElement value) && value.TryGetInt32(out int parsed)
            ? parsed
            : null;

    private static string? ReadNestedString(JsonElement root, string parentPropertyName, string propertyName)
    {
        if (!root.TryGetProperty(parentPropertyName, out JsonElement parent))
        {
            return null;
        }

        return ReadString(parent, propertyName);
    }
}
