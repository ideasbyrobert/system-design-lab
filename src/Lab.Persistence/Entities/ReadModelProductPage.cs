namespace Lab.Persistence.Entities;

public sealed class ReadModelProductPage
{
    public string ProductId { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public long ProjectionVersion { get; set; }

    public string SummaryJson { get; set; } = string.Empty;

    public DateTimeOffset ProjectedUtc { get; set; }
}
