namespace Lab.Persistence.Entities;

public sealed class User
{
    public string UserId { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    public ICollection<Cart> Carts { get; } = [];

    public ICollection<Order> Orders { get; } = [];
}
