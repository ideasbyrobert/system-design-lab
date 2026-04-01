namespace Lab.Persistence.Entities;

public sealed class OrderItem
{
    public string OrderItemId { get; set; } = string.Empty;

    public string OrderId { get; set; } = string.Empty;

    public string ProductId { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public int UnitPriceCents { get; set; }

    public Order Order { get; set; } = null!;

    public Product Product { get; set; } = null!;
}
