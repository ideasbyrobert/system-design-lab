namespace Lab.Persistence.Entities;

public sealed class InventoryRecord
{
    public string ProductId { get; set; } = string.Empty;

    public int AvailableQuantity { get; set; }

    public int ReservedQuantity { get; set; }

    public long Version { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public Product Product { get; set; } = null!;
}
