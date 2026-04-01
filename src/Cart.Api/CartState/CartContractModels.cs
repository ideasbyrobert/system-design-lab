namespace Cart.Api.CartState;

internal sealed class CartItemMutationRequest
{
    public string UserId { get; set; } = string.Empty;

    public string ProductId { get; set; } = string.Empty;

    public int Quantity { get; set; }
}

internal sealed record CartRequestInfo(
    string RunId,
    string TraceId,
    string RequestId,
    string? CorrelationId);

internal sealed record CartItemResponse(
    string ProductId,
    int Quantity,
    int UnitPriceSnapshotCents,
    int LineSubtotalCents,
    DateTimeOffset AddedUtc);

internal sealed record CartResponse(
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
    IReadOnlyList<CartItemResponse> Items,
    CartRequestInfo Request);

internal sealed record CartErrorResponse(
    string Error,
    string Detail,
    string? UserId,
    string? ProductId,
    CartRequestInfo Request);

internal sealed record CartHostInfoResponse(
    string ServiceName,
    string Region,
    string RepositoryRoot,
    CartRequestInfo Request);
