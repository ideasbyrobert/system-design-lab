namespace Lab.Shared.Queueing;

public sealed record PaymentConfirmationRetryJobPayload(
    string PaymentId,
    string OrderId,
    string? RunId = null,
    bool StatusCheckOnly = false);

public sealed record OrderHistoryProjectionUpdateJobPayload(
    string OrderId,
    string UserId,
    string? RunId = null);

public sealed record ProductPageProjectionRebuildJobPayload(
    string? Region = null,
    string? RunId = null);
