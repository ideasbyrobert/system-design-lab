namespace Lab.Persistence.Entities;

public sealed class Product
{
    public string ProductId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int PriceCents { get; set; }

    public string Category { get; set; } = string.Empty;

    public long Version { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public InventoryRecord? Inventory { get; set; }

    public ICollection<CartItem> CartItems { get; } = [];

    public ICollection<OrderItem> OrderItems { get; } = [];
}
