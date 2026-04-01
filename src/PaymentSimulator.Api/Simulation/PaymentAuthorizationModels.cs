namespace PaymentSimulator.Api.Simulation;

public sealed class PaymentAuthorizationRequest
{
    public string PaymentId { get; set; } = string.Empty;

    public string OrderId { get; set; } = string.Empty;

    public int AmountCents { get; set; }

    public string Currency { get; set; } = "USD";

    public string? CallbackUrl { get; set; }
}

internal sealed record PaymentSimulatorRequestInfo(
    string RunId,
    string TraceId,
    string RequestId,
    string? CorrelationId);

internal sealed record PaymentAuthorizationResponse(
    string PaymentId,
    string OrderId,
    string ProviderReference,
    string Mode,
    string Outcome,
    string ModeSource,
    int AttemptNumber,
    int AmountCents,
    string Currency,
    bool CallbackPending,
    int CallbackCountScheduled,
    PaymentSimulatorRequestInfo Request);

internal sealed record PaymentAuthorizationFailureResponse(
    string Error,
    string Detail,
    string PaymentId,
    string OrderId,
    string Mode,
    string ModeSource,
    string? ProviderReference,
    int AttemptNumber,
    int AmountCents,
    string Currency,
    PaymentSimulatorRequestInfo Request);

internal sealed record PaymentAuthorizationStatusResponse(
    string PaymentId,
    string OrderId,
    string Mode,
    string Outcome,
    int AttemptCount,
    string LatestProviderReference,
    int AmountCents,
    string Currency,
    string? CallbackUrl,
    IReadOnlyList<PaymentCallbackStatusResponse> Callbacks,
    PaymentSimulatorRequestInfo Request);

internal sealed record PaymentCallbackStatusResponse(
    string CallbackId,
    int SequenceNumber,
    string Status,
    DateTimeOffset DueUtc,
    DateTimeOffset? CompletedUtc,
    int DeliveryAttempts,
    string? LastError);
