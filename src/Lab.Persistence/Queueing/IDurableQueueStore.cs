namespace Lab.Persistence.Queueing;

public interface IDurableQueueStore
{
    Task<QueueJobRecord> EnqueueAsync(EnqueueQueueJobRequest request, CancellationToken cancellationToken = default);

    Task<ClaimedQueueJob?> ClaimNextAvailableAsync(ClaimNextQueueJobRequest request, CancellationToken cancellationToken = default);

    Task<QueueJobRecord> CompleteAsync(CompleteQueueJobRequest request, CancellationToken cancellationToken = default);

    Task<QueueJobRecord> FailAsync(FailQueueJobRequest request, CancellationToken cancellationToken = default);

    Task<QueueJobRecord> RescheduleAsync(RescheduleQueueJobRequest request, CancellationToken cancellationToken = default);

    Task<int> AbandonExpiredLeasesAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    Task<QueueBacklogSnapshot> GetBacklogSnapshotAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    Task<QueueStateSnapshot> GetStateSnapshotAsync(DateTimeOffset nowUtc, string? runId = null, CancellationToken cancellationToken = default);

    Task<QueueJobRecord?> GetByIdAsync(string queueJobId, CancellationToken cancellationToken = default);
}
