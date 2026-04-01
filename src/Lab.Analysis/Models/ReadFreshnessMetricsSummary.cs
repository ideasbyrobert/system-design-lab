namespace Lab.Analysis.Models;

public sealed record ReadFreshnessMetricsSummary
{
    public int ReadRequestCount { get; init; }

    public int StaleRequestCount { get; init; }

    public double? StaleRequestFraction { get; init; }

    public int ComparedResultCount { get; init; }

    public int StaleResultCount { get; init; }

    public double? StaleResultFraction { get; init; }

    public double? AverageMaxStalenessAgeMs { get; init; }

    public double? MaxObservedStalenessAgeMs { get; init; }

    public IReadOnlyList<ReadSourceFreshnessMetricsSummary> Sources { get; init; } =
        Array.Empty<ReadSourceFreshnessMetricsSummary>();
}

public sealed record ReadSourceFreshnessMetricsSummary
{
    public required string ReadSource { get; init; }

    public int RequestCount { get; init; }

    public int StaleRequestCount { get; init; }

    public double? StaleRequestFraction { get; init; }

    public double? AverageLatencyMs { get; init; }

    public double? P95LatencyMs { get; init; }

    public double? AverageMaxStalenessAgeMs { get; init; }

    public double? MaxObservedStalenessAgeMs { get; init; }
}
