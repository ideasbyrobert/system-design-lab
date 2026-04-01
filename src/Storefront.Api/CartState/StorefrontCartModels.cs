namespace Storefront.Api.CartState;

public sealed class StorefrontCartMutationRequest
{
    public string UserId { get; set; } = string.Empty;

    public string ProductId { get; set; } = string.Empty;

    public int Quantity { get; set; }
}

internal sealed record StorefrontCartServiceRequestInfo(
    string Service,
    string RunId,
    string TraceId,
    string RequestId,
    string? CorrelationId);

internal sealed record StorefrontCartItemSnapshot(
    string ProductId,
    int Quantity,
    int UnitPriceSnapshotCents,
    int LineSubtotalCents,
    DateTimeOffset AddedUtc);

internal sealed record StorefrontCartSnapshot(
    string? CartId,
    string UserId,
    string Region,
    bool Exists,
    string Status,
    string LoadOutcome,
    string MutationOutcome,
    bool Persisted,
    int DistinctItemCount,
    int TotalQuantity,
    int TotalPriceCents,
    IReadOnlyList<StorefrontCartItemSnapshot> Items,
    StorefrontCartServiceRequestInfo CartRequest);

internal enum CartClientOutcome
{
    Success,
    DomainFailure,
    Failed
}

internal sealed record CartClientResult(
    CartClientOutcome Outcome,
    StorefrontCartSnapshot? Cart,
    string? ErrorCode,
    string? ErrorDetail,
    int StatusCode,
    StorefrontCartServiceRequestInfo? CartRequest);

internal sealed record StorefrontCartItemResponse(
    string ProductId,
    int Quantity,
    int UnitPriceSnapshotCents,
    int LineSubtotalCents,
    DateTimeOffset AddedUtc);

internal sealed record StorefrontCartMutationResponse(
    string? CartId,
    string UserId,
    string Region,
    bool Exists,
    string Status,
    string LoadOutcome,
    string MutationOutcome,
    bool Persisted,
    int DistinctItemCount,
    int TotalQuantity,
    int TotalPriceCents,
    string Source,
    IReadOnlyList<StorefrontCartItemResponse> Items,
    StorefrontCartServiceRequestInfo Cart,
    ProductPages.StorefrontRequestInfo Request);

internal sealed record StorefrontCartFailureResponse(
    string Error,
    string Detail,
    string UserId,
    string ProductId,
    string Source,
    StorefrontCartServiceRequestInfo? Cart,
    ProductPages.StorefrontRequestInfo Request);
