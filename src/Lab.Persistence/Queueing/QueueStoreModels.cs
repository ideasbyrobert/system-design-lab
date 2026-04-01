namespace Lab.Persistence.Queueing;

public sealed record EnqueueQueueJobRequest(
    string QueueJobId,
    string JobType,
    string PayloadJson,
    DateTimeOffset EnqueuedUtc,
    DateTimeOffset? AvailableUtc = null);

public sealed record ClaimNextQueueJobRequest(
    string LeaseOwner,
    DateTimeOffset ClaimedUtc,
    TimeSpan LeaseDuration);

public sealed record CompleteQueueJobRequest(
    string QueueJobId,
    string LeaseOwner,
    DateTimeOffset CompletedUtc);

public sealed record FailQueueJobRequest(
    string QueueJobId,
    string LeaseOwner,
    DateTimeOffset FailedUtc,
    string Error);

public sealed record RescheduleQueueJobRequest(
    string QueueJobId,
    string LeaseOwner,
    DateTimeOffset FailedUtc,
    DateTimeOffset NextAttemptUtc,
    string Error,
    string? UpdatedPayloadJson = null);

public sealed record QueueJobRecord(
    string QueueJobId,
    string JobType,
    string PayloadJson,
    string Status,
    DateTimeOffset AvailableUtc,
    DateTimeOffset EnqueuedUtc,
    string? LeaseOwner,
    DateTimeOffset? LeaseExpiresUtc,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? CompletedUtc,
    int RetryCount,
    string? LastError);

public sealed record ClaimedQueueJob(
    QueueJobRecord Job,
    double QueueDelayMs);

public sealed record QueueBacklogSnapshot(
    int PendingCount,
    int ReadyCount,
    int DelayedCount,
    int InProgressCount,
    int CompletedCount,
    int FailedCount,
    DateTimeOffset? OldestReadyEnqueuedUtc,
    double? OldestReadyAgeMs);

public sealed record QueueStateSnapshot(
    DateTimeOffset SnapshotUtc,
    string? RunId,
    int PendingCount,
    int ReadyCount,
    int DelayedCount,
    int InProgressCount,
    int CompletedCount,
    int FailedCount,
    DateTimeOffset? OldestQueuedEnqueuedUtc,
    double? OldestQueuedAgeMs);
