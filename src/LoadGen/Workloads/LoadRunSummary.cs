namespace LoadGenTool.Workloads;

public sealed record LoadRunSummary
{
    public required string RunId { get; init; }

    public required string TargetUrl { get; init; }

    public required WorkloadMode Mode { get; init; }

    public int PlannedRequestCount { get; init; }

    public int CompletedRequestCount { get; init; }

    public int SuccessCount { get; init; }

    public int FailureCount { get; init; }

    public double? AverageLatencyMs { get; init; }

    public double? P95LatencyMs { get; init; }

    public IReadOnlyDictionary<string, int> StatusCounts { get; init; } = new Dictionary<string, int>();

    public IReadOnlyDictionary<string, int> ErrorCounts { get; init; } = new Dictionary<string, int>();
}
