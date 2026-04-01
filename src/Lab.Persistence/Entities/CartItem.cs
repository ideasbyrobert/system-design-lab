namespace Lab.Persistence.Entities;

public sealed class CartItem
{
    public string CartItemId { get; set; } = string.Empty;

    public string CartId { get; set; } = string.Empty;

    public string ProductId { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public int UnitPriceCents { get; set; }

    public DateTimeOffset AddedUtc { get; set; }

    public Cart Cart { get; set; } = null!;

    public Product Product { get; set; } = null!;
}
