using Lab.Persistence;
using Lab.Persistence.Queueing;

namespace Lab.IntegrationTests;

[TestClass]
public sealed class DurableQueueStoreIntegrationTests
{
    [TestMethod]
    public async Task AbandonExpiredLease_MakesJobClaimableFromAnotherContext()
    {
        string databasePath = await InitializeDatabaseAsync();
        DateTimeOffset enqueuedUtc = new(2026, 4, 1, 3, 0, 0, TimeSpan.Zero);
        DateTimeOffset firstClaimUtc = enqueuedUtc.AddSeconds(1);
        DateTimeOffset recoveryUtc = enqueuedUtc.AddMinutes(2);

        await using (PrimaryDbContext setupContext = CreateDbContext(databasePath))
        {
            SqliteDurableQueueStore setupStore = new(setupContext);
            await setupStore.EnqueueAsync(
                new EnqueueQueueJobRequest(
                    QueueJobId: "job-050-expired-001",
                    JobType: "publish-event",
                    PayloadJson: """{"event":"order-confirmed"}""",
                    EnqueuedUtc: enqueuedUtc));
        }

        await using (PrimaryDbContext firstWorkerContext = CreateDbContext(databasePath))
        {
            SqliteDurableQueueStore firstWorkerStore = new(firstWorkerContext);
            ClaimedQueueJob? firstClaim = await firstWorkerStore.ClaimNextAvailableAsync(
                new ClaimNextQueueJobRequest(
                    LeaseOwner: "worker-a",
                    ClaimedUtc: firstClaimUtc,
                    LeaseDuration: TimeSpan.FromSeconds(10)));

            Assert.IsNotNull(firstClaim);
            Assert.AreEqual("worker-a", firstClaim.Job.LeaseOwner);
        }

        await using (PrimaryDbContext secondWorkerContext = CreateDbContext(databasePath))
        {
            SqliteDurableQueueStore secondWorkerStore = new(secondWorkerContext);

            ClaimedQueueJob? beforeRecovery = await secondWorkerStore.ClaimNextAvailableAsync(
                new ClaimNextQueueJobRequest(
                    LeaseOwner: "worker-b",
                    ClaimedUtc: firstClaimUtc.AddSeconds(5),
                    LeaseDuration: TimeSpan.FromSeconds(30)));

            Assert.IsNull(beforeRecovery);

            int abandonedCount = await secondWorkerStore.AbandonExpiredLeasesAsync(recoveryUtc);
            Assert.AreEqual(1, abandonedCount);
        }

        await using (PrimaryDbContext recoveryContext = CreateDbContext(databasePath))
        {
            SqliteDurableQueueStore recoveryStore = new(recoveryContext);
            ClaimedQueueJob? recoveredClaim = await recoveryStore.ClaimNextAvailableAsync(
                new ClaimNextQueueJobRequest(
                    LeaseOwner: "worker-b",
                    ClaimedUtc: recoveryUtc,
                    LeaseDuration: TimeSpan.FromSeconds(30)));

            Assert.IsNotNull(recoveredClaim);
            Assert.AreEqual("job-050-expired-001", recoveredClaim.Job.QueueJobId);
            Assert.AreEqual("worker-b", recoveredClaim.Job.LeaseOwner);
            Assert.AreEqual(QueueJobStatuses.InProgress, recoveredClaim.Job.Status);
            Assert.AreEqual((recoveryUtc - enqueuedUtc).TotalMilliseconds, recoveredClaim.QueueDelayMs, 5d);
            Assert.AreEqual(recoveryUtc, recoveredClaim.Job.StartedUtc);
        }
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
}
