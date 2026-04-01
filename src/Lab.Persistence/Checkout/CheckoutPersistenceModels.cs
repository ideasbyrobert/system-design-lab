namespace Lab.Persistence.Checkout;

public sealed record InventoryReservationRequest(
    string ProductId,
    int Quantity);

public sealed record InventoryReservationSnapshot(
    string ProductId,
    int AvailableQuantity,
    int ReservedQuantity,
    long Version,
    DateTimeOffset UpdatedUtc);

public sealed record CheckoutPaymentPersistenceRequest(
    string PaymentId,
    string Provider,
    string IdempotencyKey,
    string Mode,
    string Status,
    int AmountCents,
    string? ExternalReference,
    string? ErrorCode,
    DateTimeOffset AttemptedUtc,
    DateTimeOffset? ConfirmedUtc);

public sealed record CheckoutOrderItemPersistenceRequest(
    string ProductId,
    int Quantity,
    int UnitPriceCents);

public sealed record CheckoutPersistenceRequest(
    string OrderId,
    string UserId,
    string? CartId,
    string Region,
    string OrderStatus,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? SubmittedUtc,
    IReadOnlyList<CheckoutOrderItemPersistenceRequest> Items,
    CheckoutPaymentPersistenceRequest? Payment);

public sealed record CheckoutPersistenceFailure(
    string Code,
    string Detail,
    string? ProductId);

public sealed record InventoryReservationResult(
    bool Succeeded,
    IReadOnlyList<InventoryReservationSnapshot> Inventory,
    CheckoutPersistenceFailure? Failure)
{
    public static InventoryReservationResult Success(IReadOnlyList<InventoryReservationSnapshot> inventory) =>
        new(true, inventory, null);

    public static InventoryReservationResult Fail(string code, string detail, string? productId = null) =>
        new(false, [], new CheckoutPersistenceFailure(code, detail, productId));
}

public sealed record CheckoutPersistenceResult(
    bool Succeeded,
    string? OrderId,
    string? PaymentId,
    int? TotalPriceCents,
    IReadOnlyList<InventoryReservationSnapshot> Inventory,
    CheckoutPersistenceFailure? Failure)
{
    public static CheckoutPersistenceResult Success(
        string orderId,
        string? paymentId,
        int totalPriceCents,
        IReadOnlyList<InventoryReservationSnapshot> inventory,
        string? queueJobId = null) =>
        new(true, orderId, paymentId, totalPriceCents, inventory, null)
        {
            QueueJobId = queueJobId
        };

    public static CheckoutPersistenceResult Fail(
        string code,
        string detail,
        string? productId = null) =>
        new(false, null, null, null, [], new CheckoutPersistenceFailure(code, detail, productId));

    public string? QueueJobId { get; init; }
}
