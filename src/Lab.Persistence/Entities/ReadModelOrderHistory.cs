namespace Lab.Persistence.Entities;

public sealed class ReadModelOrderHistory
{
    public string OrderId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset OrderCreatedUtc { get; set; }

    public long ProjectionVersion { get; set; }

    public string SummaryJson { get; set; } = string.Empty;

    public DateTimeOffset ProjectedUtc { get; set; }
}
