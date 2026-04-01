namespace Lab.Persistence.Entities;

public sealed class Cart
{
    public string CartId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public User User { get; set; } = null!;

    public ICollection<CartItem> Items { get; } = [];
}
