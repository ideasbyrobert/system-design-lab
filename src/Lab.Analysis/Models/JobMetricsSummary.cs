namespace Lab.Analysis.Models;

public sealed record JobMetricsSummary
{
    public int JobCount { get; init; }

    public double? AverageQueueDelayMs { get; init; }

    public double? P95QueueDelayMs { get; init; }

    public double? AverageExecutionMs { get; init; }

    public double? P95ExecutionMs { get; init; }

    public IReadOnlyDictionary<string, int> RetryCountDistribution { get; init; } = new Dictionary<string, int>();
}
