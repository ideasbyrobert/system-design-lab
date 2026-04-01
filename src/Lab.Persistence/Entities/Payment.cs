namespace Lab.Persistence.Entities;

public sealed class Payment
{
    public string PaymentId { get; set; } = string.Empty;

    public string OrderId { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string? IdempotencyKey { get; set; }

    public string? Mode { get; set; }

    public string Status { get; set; } = string.Empty;

    public int AmountCents { get; set; }

    public string? ExternalReference { get; set; }

    public string? ErrorCode { get; set; }

    public DateTimeOffset AttemptedUtc { get; set; }

    public DateTimeOffset? ConfirmedUtc { get; set; }

    public Order Order { get; set; } = null!;
}
