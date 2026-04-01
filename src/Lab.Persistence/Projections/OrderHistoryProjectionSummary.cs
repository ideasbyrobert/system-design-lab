namespace Lab.Persistence.Projections;

public sealed record OrderHistoryProjectionPaymentSummary(
    string PaymentId,
    string Provider,
    string Status,
    string? Mode,
    int AmountCents,
    string? ProviderReference,
    string? ErrorCode,
    DateTimeOffset AttemptedUtc,
    DateTimeOffset? ConfirmedUtc);

public sealed record OrderHistoryProjectionItemSummary(
    string ProductId,
    string ProductName,
    int Quantity,
    int UnitPriceCents,
    int LineSubtotalCents);

public sealed record OrderHistoryProjectionSourceVersions(long ProjectionVersion);

public sealed record OrderHistoryProjectionSummary(
    string OrderId,
    string UserId,
    string Region,
    string Status,
    int TotalAmountCents,
    int ItemCount,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? SubmittedUtc,
    OrderHistoryProjectionPaymentSummary? Payment,
    IReadOnlyList<OrderHistoryProjectionItemSummary> Items,
    OrderHistoryProjectionSourceVersions Versions);
