using System.Text.Json.Serialization;
using Lab.Shared.RegionalReads;
using Storefront.Api.ProductPages;

namespace Storefront.Api.OrderHistory;

internal sealed record StorefrontOrderHistoryPaymentInfo(
    string PaymentId,
    string Provider,
    string Status,
    string? Mode,
    int AmountCents,
    string? ProviderReference,
    string? ErrorCode,
    DateTimeOffset AttemptedUtc,
    DateTimeOffset? ConfirmedUtc);

internal sealed record StorefrontOrderHistoryItemInfo(
    string ProductId,
    string ProductName,
    int Quantity,
    int UnitPriceCents,
    int LineSubtotalCents);

internal sealed record StorefrontOrderHistorySnapshot(
    string OrderId,
    string UserId,
    string Region,
    string Status,
    int TotalAmountCents,
    int ItemCount,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? SubmittedUtc,
    long ProjectionVersion,
    DateTimeOffset ProjectedUtc,
    IReadOnlyList<StorefrontOrderHistoryItemInfo> Items)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StorefrontOrderHistoryPaymentInfo? Payment { get; init; }
}

internal sealed record StorefrontOrderHistoryResponse(
    string UserId,
    string Source,
    StorefrontReadFreshnessInfo Freshness,
    int OrderCount,
    DateTimeOffset? OldestProjectedUtc,
    DateTimeOffset? NewestProjectedUtc,
    IReadOnlyList<StorefrontOrderHistorySnapshot> Orders,
    StorefrontRequestInfo Request);

internal sealed record StorefrontOrderHistoryFailureResponse(
    string Error,
    string Detail,
    bool ContractSatisfied,
    string UserId,
    string Source,
    StorefrontReadFreshnessInfo? Freshness,
    StorefrontRequestInfo Request);

internal sealed record StorefrontOrderHistoryReadResult(
    IReadOnlyList<StorefrontOrderHistorySnapshot> Orders,
    StorefrontReadFreshnessInfo Freshness,
    RegionalReadPreference ReadPreference);
