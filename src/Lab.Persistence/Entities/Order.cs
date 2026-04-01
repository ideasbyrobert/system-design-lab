namespace Lab.Persistence.Entities;

public sealed class Order
{
    public string OrderId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string? CartId { get; set; }

    public string Region { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public int TotalPriceCents { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset? SubmittedUtc { get; set; }

    public User User { get; set; } = null!;

    public ICollection<OrderItem> Items { get; } = [];

    public ICollection<Payment> Payments { get; } = [];
}
