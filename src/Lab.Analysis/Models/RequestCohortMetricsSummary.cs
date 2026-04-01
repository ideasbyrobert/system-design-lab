namespace Lab.Analysis.Models;

public sealed record RequestCohortMetricsSummary
{
    public int RequestCount { get; init; }

    public int CompletedRequestCount { get; init; }

    public double? Fraction { get; init; }

    public double? AverageLatencyMs { get; init; }

    public double? P95LatencyMs { get; init; }

    public double? ThroughputPerSecond { get; init; }

    public double? AverageConcurrency { get; init; }

    public IReadOnlyDictionary<string, int> ErrorCounts { get; init; } = new Dictionary<string, int>();
}
