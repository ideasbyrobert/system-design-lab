namespace Lab.Analysis.Models;

public sealed record RequestMetricsSummary
{
    public int RequestCount { get; init; }

    public int CompletedRequestCount { get; init; }

    public DateTimeOffset? WindowStartUtc { get; init; }

    public DateTimeOffset? WindowEndUtc { get; init; }

    public double? WindowDurationMs { get; init; }

    public double? AverageLatencyMs { get; init; }

    public double? P50LatencyMs { get; init; }

    public double? P95LatencyMs { get; init; }

    public double? P99LatencyMs { get; init; }

    public double? ThroughputPerSecond { get; init; }

    public double? AverageConcurrency { get; init; }

    public double? RateLimitedFraction { get; init; }

    public double? CacheHitRate { get; init; }

    public double? CacheMissRate { get; init; }

    public IReadOnlyDictionary<string, int> ErrorCounts { get; init; } = new Dictionary<string, int>();
}
