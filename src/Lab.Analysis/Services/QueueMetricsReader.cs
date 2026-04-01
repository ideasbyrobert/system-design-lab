using Lab.Analysis.Models;
using Lab.Persistence;
using Lab.Persistence.Queueing;

namespace Lab.Analysis.Services;

public sealed class QueueMetricsReader
{
    public async Task<QueueMetricsSummary> ReadAsync(
        string? primaryDatabasePath,
        DateTimeOffset snapshotUtc,
        string? runId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(primaryDatabasePath))
        {
            return CreateUnavailableSummary(snapshotUtc, runId, "Primary database path was not provided.");
        }

        if (!File.Exists(primaryDatabasePath))
        {
            return CreateUnavailableSummary(snapshotUtc, runId, $"Primary database file '{primaryDatabasePath}' does not exist.");
        }

        PrimaryDbContextFactory dbContextFactory = new();
        await using PrimaryDbContext dbContext = dbContextFactory.CreateDbContext(primaryDatabasePath);
        SqliteDurableQueueStore queueStore = new(dbContext);
        QueueStateSnapshot snapshot = await queueStore.GetStateSnapshotAsync(snapshotUtc, runId, cancellationToken);

        return new QueueMetricsSummary
        {
            Captured = true,
            CaptureReason = null,
            SnapshotUtc = snapshot.SnapshotUtc,
            FilterRunId = snapshot.RunId,
            PendingCount = snapshot.PendingCount,
            ReadyCount = snapshot.ReadyCount,
            DelayedCount = snapshot.DelayedCount,
            InProgressCount = snapshot.InProgressCount,
            CompletedCount = snapshot.CompletedCount,
            FailedCount = snapshot.FailedCount,
            OldestQueuedEnqueuedUtc = snapshot.OldestQueuedEnqueuedUtc,
            OldestQueuedAgeMs = snapshot.OldestQueuedAgeMs
        };
    }

    private static QueueMetricsSummary CreateUnavailableSummary(DateTimeOffset snapshotUtc, string? runId, string reason) =>
        new()
        {
            Captured = false,
            CaptureReason = reason,
            SnapshotUtc = snapshotUtc,
            FilterRunId = NormalizeOptionalText(runId)
        };

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
