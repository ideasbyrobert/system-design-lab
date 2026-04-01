using Lab.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Lab.Persistence.Queueing;

public sealed class SqliteDurableQueueStore(PrimaryDbContext dbContext) : IDurableQueueStore
{
    private const int ClaimRetries = 8;

    public async Task<QueueJobRecord> EnqueueAsync(EnqueueQueueJobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateEnqueueRequest(request);

        QueueJob entity = new()
        {
            QueueJobId = request.QueueJobId.Trim(),
            JobType = request.JobType.Trim(),
            PayloadJson = request.PayloadJson,
            Status = QueueJobStatuses.Pending,
            AvailableUtc = request.AvailableUtc ?? request.EnqueuedUtc,
            EnqueuedUtc = request.EnqueuedUtc,
            RetryCount = 0
        };

        dbContext.QueueJobs.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToRecord(entity);
    }

    public async Task<ClaimedQueueJob?> ClaimNextAvailableAsync(ClaimNextQueueJobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateClaimRequest(request);

        DateTimeOffset leaseExpiresUtc = request.ClaimedUtc.Add(request.LeaseDuration);

        for (int attempt = 0; attempt < ClaimRetries; attempt++)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();

            QueueJob[] pendingJobs = (await dbContext.QueueJobs
                .AsNoTracking()
                .Where(item => item.Status == QueueJobStatuses.Pending)
                .ToListAsync(cancellationToken))
                .ToArray();

            QueueJob? candidate = pendingJobs
                .Where(item => item.AvailableUtc <= request.ClaimedUtc)
                .OrderBy(item => item.AvailableUtc)
                .ThenBy(item => item.EnqueuedUtc)
                .ThenBy(item => item.QueueJobId, StringComparer.Ordinal)
                .FirstOrDefault();

            if (candidate is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            int rowsAffected = await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE queue_jobs
                SET status = {QueueJobStatuses.InProgress},
                    lease_owner = {request.LeaseOwner},
                    lease_expires_utc = {leaseExpiresUtc},
                    started_utc = {request.ClaimedUtc},
                    completed_utc = NULL,
                    last_error = NULL
                WHERE queue_job_id = {candidate.QueueJobId}
                  AND status = {QueueJobStatuses.Pending}
                  AND available_utc <= {request.ClaimedUtc};
                """,
                cancellationToken);

            if (rowsAffected == 1)
            {
                dbContext.ChangeTracker.Clear();

                QueueJob claimed = await dbContext.QueueJobs
                    .AsNoTracking()
                    .SingleAsync(item => item.QueueJobId == candidate.QueueJobId, cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                return new ClaimedQueueJob(
                    ToRecord(claimed),
                    Math.Max(0d, (request.ClaimedUtc - claimed.EnqueuedUtc).TotalMilliseconds));
            }

            await transaction.RollbackAsync(cancellationToken);
        }

        return null;
    }

    public async Task<QueueJobRecord> CompleteAsync(CompleteQueueJobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCompletionRequest(request);

        QueueJob job = await GetOwnedInProgressJobAsync(request.QueueJobId, request.LeaseOwner, cancellationToken);
        job.Status = QueueJobStatuses.Completed;
        job.CompletedUtc = request.CompletedUtc;
        job.LeaseOwner = null;
        job.LeaseExpiresUtc = null;
        job.LastError = null;

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToRecord(job);
    }

    public async Task<QueueJobRecord> FailAsync(FailQueueJobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateFailureRequest(request);

        QueueJob job = await GetOwnedInProgressJobAsync(request.QueueJobId, request.LeaseOwner, cancellationToken);
        job.Status = QueueJobStatuses.Failed;
        job.CompletedUtc = request.FailedUtc;
        job.LeaseOwner = null;
        job.LeaseExpiresUtc = null;
        job.LastError = request.Error.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToRecord(job);
    }

    public async Task<QueueJobRecord> RescheduleAsync(RescheduleQueueJobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRescheduleRequest(request);

        QueueJob job = await GetOwnedInProgressJobAsync(request.QueueJobId, request.LeaseOwner, cancellationToken);
        job.Status = QueueJobStatuses.Pending;
        job.AvailableUtc = request.NextAttemptUtc;
        job.LeaseOwner = null;
        job.LeaseExpiresUtc = null;
        job.StartedUtc = null;
        job.CompletedUtc = null;
        job.RetryCount += 1;
        job.LastError = request.Error.Trim();

        if (request.UpdatedPayloadJson is not null)
        {
            job.PayloadJson = request.UpdatedPayloadJson;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToRecord(job);
    }

    public async Task<int> AbandonExpiredLeasesAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        dbContext.ChangeTracker.Clear();

        int rowsAffected = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE queue_jobs
            SET status = {QueueJobStatuses.Pending},
                available_utc = {nowUtc},
                lease_owner = NULL,
                lease_expires_utc = NULL,
                started_utc = NULL,
                completed_utc = NULL,
                last_error = {"lease_expired"}
            WHERE status = {QueueJobStatuses.InProgress}
              AND lease_expires_utc IS NOT NULL
              AND lease_expires_utc <= {nowUtc};
            """,
            cancellationToken);

        dbContext.ChangeTracker.Clear();
        return rowsAffected;
    }

    public async Task<QueueBacklogSnapshot> GetBacklogSnapshotAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        QueueJobRecord[] jobs = (await dbContext.QueueJobs
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .Select(ToRecord)
            .ToArray();

        QueueJobRecord[] pendingJobs = jobs.Where(item => item.Status == QueueJobStatuses.Pending).ToArray();
        QueueJobRecord[] readyJobs = pendingJobs.Where(item => item.AvailableUtc <= nowUtc).ToArray();
        DateTimeOffset? oldestReadyEnqueuedUtc = readyJobs.Length > 0
            ? readyJobs.MinBy(item => item.EnqueuedUtc)?.EnqueuedUtc
            : null;

        return new QueueBacklogSnapshot(
            PendingCount: pendingJobs.Length,
            ReadyCount: readyJobs.Length,
            DelayedCount: pendingJobs.Length - readyJobs.Length,
            InProgressCount: jobs.Count(item => item.Status == QueueJobStatuses.InProgress),
            CompletedCount: jobs.Count(item => item.Status == QueueJobStatuses.Completed),
            FailedCount: jobs.Count(item => item.Status == QueueJobStatuses.Failed),
            OldestReadyEnqueuedUtc: oldestReadyEnqueuedUtc,
            OldestReadyAgeMs: oldestReadyEnqueuedUtc.HasValue
                ? Math.Max(0d, (nowUtc - oldestReadyEnqueuedUtc.Value).TotalMilliseconds)
                : null);
    }

    public async Task<QueueStateSnapshot> GetStateSnapshotAsync(
        DateTimeOffset nowUtc,
        string? runId = null,
        CancellationToken cancellationToken = default)
    {
        QueueJobRecord[] jobs = (await dbContext.QueueJobs
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .Select(ToRecord)
            .Where(item => MatchesRunId(item, runId))
            .ToArray();

        QueueJobRecord[] pendingJobs = jobs.Where(item => item.Status == QueueJobStatuses.Pending).ToArray();
        QueueJobRecord[] readyJobs = pendingJobs.Where(item => item.AvailableUtc <= nowUtc).ToArray();
        DateTimeOffset? oldestQueuedEnqueuedUtc = pendingJobs.Length > 0
            ? pendingJobs.MinBy(item => item.EnqueuedUtc)?.EnqueuedUtc
            : null;

        return new QueueStateSnapshot(
            SnapshotUtc: nowUtc,
            RunId: NormalizeOptionalText(runId),
            PendingCount: pendingJobs.Length,
            ReadyCount: readyJobs.Length,
            DelayedCount: pendingJobs.Length - readyJobs.Length,
            InProgressCount: jobs.Count(item => item.Status == QueueJobStatuses.InProgress),
            CompletedCount: jobs.Count(item => item.Status == QueueJobStatuses.Completed),
            FailedCount: jobs.Count(item => item.Status == QueueJobStatuses.Failed),
            OldestQueuedEnqueuedUtc: oldestQueuedEnqueuedUtc,
            OldestQueuedAgeMs: oldestQueuedEnqueuedUtc.HasValue
                ? Math.Max(0d, (nowUtc - oldestQueuedEnqueuedUtc.Value).TotalMilliseconds)
                : null);
    }

    public async Task<QueueJobRecord?> GetByIdAsync(string queueJobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueJobId);

        QueueJob? job = await dbContext.QueueJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.QueueJobId == queueJobId.Trim(), cancellationToken);

        return job is null ? null : ToRecord(job);
    }

    private async Task<QueueJob> GetOwnedInProgressJobAsync(string queueJobId, string leaseOwner, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        QueueJob? job = await dbContext.QueueJobs.SingleOrDefaultAsync(item => item.QueueJobId == queueJobId.Trim(), cancellationToken);

        if (job is null)
        {
            throw new InvalidOperationException($"Queue job '{queueJobId}' does not exist.");
        }

        if (!string.Equals(job.Status, QueueJobStatuses.InProgress, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Queue job '{queueJobId}' is not in progress. Current status is '{job.Status}'.");
        }

        if (!string.Equals(job.LeaseOwner, leaseOwner.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Queue job '{queueJobId}' is not leased by '{leaseOwner}'.");
        }

        return job;
    }

    private static QueueJobRecord ToRecord(QueueJob entity) =>
        new(
            QueueJobId: entity.QueueJobId,
            JobType: entity.JobType,
            PayloadJson: entity.PayloadJson,
            Status: entity.Status,
            AvailableUtc: entity.AvailableUtc,
            EnqueuedUtc: entity.EnqueuedUtc,
            LeaseOwner: entity.LeaseOwner,
            LeaseExpiresUtc: entity.LeaseExpiresUtc,
            StartedUtc: entity.StartedUtc,
            CompletedUtc: entity.CompletedUtc,
            RetryCount: entity.RetryCount,
            LastError: entity.LastError);

    private static void ValidateEnqueueRequest(EnqueueQueueJobRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.QueueJobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.JobType);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PayloadJson);
    }

    private static void ValidateClaimRequest(ClaimNextQueueJobRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LeaseOwner);

        if (request.LeaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request.LeaseDuration), "Lease duration must be positive.");
        }
    }

    private static void ValidateCompletionRequest(CompleteQueueJobRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.QueueJobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LeaseOwner);
    }

    private static void ValidateFailureRequest(FailQueueJobRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.QueueJobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LeaseOwner);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Error);
    }

    private static void ValidateRescheduleRequest(RescheduleQueueJobRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.QueueJobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LeaseOwner);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Error);

        if (request.UpdatedPayloadJson is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request.UpdatedPayloadJson);
        }

        if (request.NextAttemptUtc < request.FailedUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(request.NextAttemptUtc), "Next attempt must not be earlier than the failure time.");
        }
    }

    private static bool MatchesRunId(QueueJobRecord job, string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return true;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(job.PayloadJson);

            if (document.RootElement.TryGetProperty("runId", out JsonElement runIdElement) &&
                runIdElement.ValueKind == JsonValueKind.String)
            {
                string? payloadRunId = NormalizeOptionalText(runIdElement.GetString());
                return string.Equals(payloadRunId, NormalizeOptionalText(runId), StringComparison.Ordinal);
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
