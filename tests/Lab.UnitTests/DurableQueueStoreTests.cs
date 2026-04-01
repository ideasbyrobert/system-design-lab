using Lab.Persistence;
using Lab.Persistence.Queueing;
using System.Text.Json;

namespace Lab.UnitTests;

[TestClass]
public sealed class DurableQueueStoreTests
{
    [TestMethod]
    public async Task EnqueueClaimCompleteAndSnapshot_WorkAsExpected()
    {
        string databasePath = await InitializeDatabaseAsync();
        DateTimeOffset enqueuedUtc = new(2026, 4, 1, 1, 0, 0, TimeSpan.Zero);
        DateTimeOffset claimUtc = enqueuedUtc.AddSeconds(5);
        DateTimeOffset completeUtc = claimUtc.AddSeconds(3);

        await using PrimaryDbContext enqueueContext = CreateDbContext(databasePath);
        SqliteDurableQueueStore enqueueStore = new(enqueueContext);

        QueueJobRecord enqueued = await enqueueStore.EnqueueAsync(
            new EnqueueQueueJobRequest(
                QueueJobId: "job-050-001",
                JobType: "send-email",
                PayloadJson: """{"email":"user@example.test"}""",
                EnqueuedUtc: enqueuedUtc));

        Assert.AreEqual(QueueJobStatuses.Pending, enqueued.Status);
        Assert.AreEqual(enqueuedUtc, enqueued.AvailableUtc);

        QueueBacklogSnapshot beforeClaim = await enqueueStore.GetBacklogSnapshotAsync(claimUtc);
        Assert.AreEqual(1, beforeClaim.PendingCount);
        Assert.AreEqual(1, beforeClaim.ReadyCount);
        Assert.AreEqual(0, beforeClaim.InProgressCount);
        Assert.AreEqual(enqueuedUtc, beforeClaim.OldestReadyEnqueuedUtc);
        Assert.AreEqual(5000d, beforeClaim.OldestReadyAgeMs!.Value, 0.001d);

        ClaimedQueueJob? claimed = await enqueueStore.ClaimNextAvailableAsync(
            new ClaimNextQueueJobRequest(
                LeaseOwner: "worker-a",
                ClaimedUtc: claimUtc,
                LeaseDuration: TimeSpan.FromMinutes(1)));

        Assert.IsNotNull(claimed);
        Assert.AreEqual("job-050-001", claimed.Job.QueueJobId);
        Assert.AreEqual(QueueJobStatuses.InProgress, claimed.Job.Status);
        Assert.AreEqual("worker-a", claimed.Job.LeaseOwner);
        Assert.AreEqual(claimUtc, claimed.Job.StartedUtc);
        Assert.AreEqual(5000d, claimed.QueueDelayMs, 0.001d);

        QueueBacklogSnapshot duringLease = await enqueueStore.GetBacklogSnapshotAsync(claimUtc);
        Assert.AreEqual(0, duringLease.PendingCount);
        Assert.AreEqual(1, duringLease.InProgressCount);

        QueueJobRecord completed = await enqueueStore.CompleteAsync(
            new CompleteQueueJobRequest(
                QueueJobId: "job-050-001",
                LeaseOwner: "worker-a",
                CompletedUtc: completeUtc));

        Assert.AreEqual(QueueJobStatuses.Completed, completed.Status);
        Assert.AreEqual(completeUtc, completed.CompletedUtc);
        Assert.IsNull(completed.LeaseOwner);
        Assert.IsNull(completed.LeaseExpiresUtc);

        QueueBacklogSnapshot afterCompletion = await enqueueStore.GetBacklogSnapshotAsync(completeUtc);
        Assert.AreEqual(0, afterCompletion.PendingCount);
        Assert.AreEqual(0, afterCompletion.InProgressCount);
        Assert.AreEqual(1, afterCompletion.CompletedCount);

        QueueStateSnapshot stateSnapshot = await enqueueStore.GetStateSnapshotAsync(completeUtc);
        Assert.AreEqual(0, stateSnapshot.PendingCount);
        Assert.AreEqual(0, stateSnapshot.InProgressCount);
        Assert.AreEqual(1, stateSnapshot.CompletedCount);
        Assert.IsNull(stateSnapshot.OldestQueuedEnqueuedUtc);
        Assert.IsNull(stateSnapshot.OldestQueuedAgeMs);
    }

    [TestMethod]
    public async Task RescheduleAndFail_UpdateRetryAndTerminalStateExplicitly()
    {
        string databasePath = await InitializeDatabaseAsync();
        DateTimeOffset enqueuedUtc = new(2026, 4, 1, 2, 0, 0, TimeSpan.Zero);
        DateTimeOffset claimUtc = enqueuedUtc.AddSeconds(2);
        DateTimeOffset failedUtc = claimUtc.AddSeconds(1);
        DateTimeOffset nextAttemptUtc = failedUtc.AddMinutes(2);
        DateTimeOffset reClaimUtc = nextAttemptUtc;

        await using PrimaryDbContext dbContext = CreateDbContext(databasePath);
        SqliteDurableQueueStore store = new(dbContext);

        await store.EnqueueAsync(
            new EnqueueQueueJobRequest(
                QueueJobId: "job-050-002",
                JobType: "capture-payment",
                PayloadJson: """{"orderId":"order-050-002"}""",
                EnqueuedUtc: enqueuedUtc));

        ClaimedQueueJob? firstClaim = await store.ClaimNextAvailableAsync(
            new ClaimNextQueueJobRequest(
                LeaseOwner: "worker-a",
                ClaimedUtc: claimUtc,
                LeaseDuration: TimeSpan.FromMinutes(1)));

        Assert.IsNotNull(firstClaim);

        QueueJobRecord rescheduled = await store.RescheduleAsync(
            new RescheduleQueueJobRequest(
                QueueJobId: "job-050-002",
                LeaseOwner: "worker-a",
                FailedUtc: failedUtc,
                NextAttemptUtc: nextAttemptUtc,
                Error: "temporary_upstream_failure",
                UpdatedPayloadJson: """{"orderId":"order-050-002","retryMode":"status-check"}"""));

        Assert.AreEqual(QueueJobStatuses.Pending, rescheduled.Status);
        Assert.AreEqual(1, rescheduled.RetryCount);
        Assert.AreEqual(nextAttemptUtc, rescheduled.AvailableUtc);
        Assert.IsNull(rescheduled.LeaseOwner);
        Assert.IsNull(rescheduled.StartedUtc);
        Assert.AreEqual("temporary_upstream_failure", rescheduled.LastError);
        Assert.AreEqual("""{"orderId":"order-050-002","retryMode":"status-check"}""", rescheduled.PayloadJson);

        ClaimedQueueJob? tooEarlyClaim = await store.ClaimNextAvailableAsync(
            new ClaimNextQueueJobRequest(
                LeaseOwner: "worker-b",
                ClaimedUtc: failedUtc.AddSeconds(30),
                LeaseDuration: TimeSpan.FromMinutes(1)));

        Assert.IsNull(tooEarlyClaim);

        ClaimedQueueJob? secondClaim = await store.ClaimNextAvailableAsync(
            new ClaimNextQueueJobRequest(
                LeaseOwner: "worker-b",
                ClaimedUtc: reClaimUtc,
                LeaseDuration: TimeSpan.FromMinutes(1)));

        Assert.IsNotNull(secondClaim);
        Assert.AreEqual("worker-b", secondClaim.Job.LeaseOwner);
        Assert.AreEqual(QueueJobStatuses.InProgress, secondClaim.Job.Status);
        Assert.AreEqual((reClaimUtc - enqueuedUtc).TotalMilliseconds, secondClaim.QueueDelayMs, 5d);

        QueueJobRecord failed = await store.FailAsync(
            new FailQueueJobRequest(
                QueueJobId: "job-050-002",
                LeaseOwner: "worker-b",
                FailedUtc: reClaimUtc.AddSeconds(1),
                Error: "permanent_validation_failure"));

        Assert.AreEqual(QueueJobStatuses.Failed, failed.Status);
        Assert.AreEqual(1, failed.RetryCount);
        Assert.AreEqual("permanent_validation_failure", failed.LastError);
        Assert.IsNotNull(failed.CompletedUtc);

        QueueBacklogSnapshot snapshot = await store.GetBacklogSnapshotAsync(reClaimUtc.AddSeconds(1));
        Assert.AreEqual(0, snapshot.PendingCount);
        Assert.AreEqual(0, snapshot.InProgressCount);
        Assert.AreEqual(1, snapshot.FailedCount);
    }

    [TestMethod]
    public async Task StateSnapshot_FiltersByRunId_And_TracksOldestQueuedItem()
    {
        string databasePath = await InitializeDatabaseAsync();
        DateTimeOffset snapshotUtc = new(2026, 4, 1, 4, 0, 0, TimeSpan.Zero);

        await using PrimaryDbContext dbContext = CreateDbContext(databasePath);
        SqliteDurableQueueStore store = new(dbContext);

        await store.EnqueueAsync(new EnqueueQueueJobRequest(
            QueueJobId: "job-054-state-001",
            JobType: "payment-confirmation-retry",
            PayloadJson: Serialize(new { paymentId = "pay-1", orderId = "order-1", runId = "run-a" }),
            EnqueuedUtc: snapshotUtc.AddSeconds(-12)));

        await store.EnqueueAsync(new EnqueueQueueJobRequest(
            QueueJobId: "job-054-state-002",
            JobType: "payment-confirmation-retry",
            PayloadJson: Serialize(new { paymentId = "pay-2", orderId = "order-2", runId = "run-a" }),
            EnqueuedUtc: snapshotUtc.AddSeconds(-8),
            AvailableUtc: snapshotUtc.AddSeconds(5)));

        await store.EnqueueAsync(new EnqueueQueueJobRequest(
            QueueJobId: "job-054-state-003",
            JobType: "payment-confirmation-retry",
            PayloadJson: Serialize(new { paymentId = "pay-3", orderId = "order-3", runId = "run-b" }),
            EnqueuedUtc: snapshotUtc.AddSeconds(-20)));

        QueueStateSnapshot filtered = await store.GetStateSnapshotAsync(snapshotUtc, "run-a");

        Assert.AreEqual("run-a", filtered.RunId);
        Assert.AreEqual(2, filtered.PendingCount);
        Assert.AreEqual(1, filtered.ReadyCount);
        Assert.AreEqual(1, filtered.DelayedCount);
        Assert.AreEqual(snapshotUtc.AddSeconds(-12), filtered.OldestQueuedEnqueuedUtc);
        Assert.AreEqual(12_000d, filtered.OldestQueuedAgeMs!.Value, 0.001d);
    }

    private static async Task<string> InitializeDatabaseAsync()
    {
        string root = CreateUniqueTempDirectory();
        string databasePath = Path.Combine(root, "primary.db");
        PrimaryDbContextFactory dbContextFactory = new();
        PrimaryDatabaseInitializer initializer = new(dbContextFactory);
        await initializer.InitializeAsync(databasePath);
        return databasePath;
    }

    private static PrimaryDbContext CreateDbContext(string databasePath)
    {
        PrimaryDbContextFactory factory = new();
        return factory.CreateDbContext(databasePath);
    }

    private static string CreateUniqueTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
