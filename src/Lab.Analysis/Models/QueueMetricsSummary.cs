namespace Lab.Analysis.Models;

public sealed record QueueMetricsSummary
{
    public bool Captured { get; init; }

    public string? CaptureReason { get; init; }

    public DateTimeOffset? SnapshotUtc { get; init; }

    public string? FilterRunId { get; init; }

    public int PendingCount { get; init; }

    public int ReadyCount { get; init; }

    public int DelayedCount { get; init; }

    public int InProgressCount { get; init; }

    public int CompletedCount { get; init; }

    public int FailedCount { get; init; }

    public DateTimeOffset? OldestQueuedEnqueuedUtc { get; init; }

    public double? OldestQueuedAgeMs { get; init; }
}
