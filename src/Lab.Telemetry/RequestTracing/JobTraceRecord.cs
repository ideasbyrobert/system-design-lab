namespace Lab.Telemetry.RequestTracing;

public sealed record class JobTraceRecord
{
    public required string RunId { get; init; }

    public required string TraceId { get; init; }

    public required string JobId { get; init; }

    public required string JobType { get; init; }

    public required string Region { get; init; }

    public required string Service { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset EnqueuedUtc { get; init; }

    public DateTimeOffset? DequeuedUtc { get; init; }

    public DateTimeOffset? ExecutionStartUtc { get; init; }

    public DateTimeOffset? ExecutionEndUtc { get; init; }

    public double? QueueDelayMs { get; init; }

    public double? ExecutionMs { get; init; }

    public int RetryCount { get; init; }

    public required bool ContractSatisfied { get; init; }

    public IReadOnlyList<DependencyCallRecord> DependencyCalls { get; init; } = Array.Empty<DependencyCallRecord>();

    public IReadOnlyList<StageTimingRecord> StageTimings { get; init; } = Array.Empty<StageTimingRecord>();

    public string? ErrorCode { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}
