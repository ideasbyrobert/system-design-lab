using Lab.Persistence.Queueing;
using Lab.Shared.Configuration;
using Lab.Telemetry.Persistence;
using Lab.Telemetry.RequestTracing;
using Microsoft.Extensions.Options;
using Worker.Jobs;

namespace Worker;

public sealed class WorkerQueueProcessor(
    ILogger<WorkerQueueProcessor> logger,
    IServiceScopeFactory scopeFactory,
    EnvironmentLayout layout,
    IJobTraceWriter jobTraceWriter,
    IOptions<QueueOptions> queueOptions,
    TimeProvider timeProvider)
{
    private readonly string _leaseOwner = $"worker-{Guid.NewGuid():N}";

    public async Task<int> ProcessAvailableJobsAsync(CancellationToken cancellationToken)
    {
        int processedCount = 0;

        using (IServiceScope recoveryScope = scopeFactory.CreateScope())
        {
            IDurableQueueStore queueStore = recoveryScope.ServiceProvider.GetRequiredService<IDurableQueueStore>();
            int abandonedCount = await queueStore.AbandonExpiredLeasesAsync(timeProvider.GetUtcNow(), cancellationToken);

            if (abandonedCount > 0)
            {
                logger.LogWarning("Recovered {Count} queue jobs with expired leases.", abandonedCount);
            }
        }

        for (int index = 0; index < Math.Max(1, queueOptions.Value.MaxDequeueBatchSize); index++)
        {
            ClaimedQueueJob? claimedJob;

            using (IServiceScope claimScope = scopeFactory.CreateScope())
            {
                IDurableQueueStore queueStore = claimScope.ServiceProvider.GetRequiredService<IDurableQueueStore>();
                claimedJob = await queueStore.ClaimNextAvailableAsync(
                    new ClaimNextQueueJobRequest(
                        LeaseOwner: _leaseOwner,
                        ClaimedUtc: timeProvider.GetUtcNow(),
                        LeaseDuration: TimeSpan.FromSeconds(Math.Max(1, queueOptions.Value.LeaseDurationSeconds))),
                    cancellationToken);
            }

            if (claimedJob is null)
            {
                break;
            }

            processedCount++;
            await ProcessClaimedJobAsync(claimedJob, cancellationToken);
        }

        return processedCount;
    }

    private async Task ProcessClaimedJobAsync(ClaimedQueueJob claimedJob, CancellationToken cancellationToken)
    {
        DateTimeOffset dequeuedUtc = claimedJob.Job.StartedUtc ?? timeProvider.GetUtcNow();
        DateTimeOffset executionStartUtc = timeProvider.GetUtcNow();
        long executionStartTimestamp = timeProvider.GetTimestamp();
        WorkerJobTraceBuilder trace = new(timeProvider);

        QueueJobRecord persistedJob;
        WorkerJobExecutionResult executionResult;

        try
        {
            using IServiceScope processingScope = scopeFactory.CreateScope();
            WorkerJobDispatcher dispatcher = processingScope.ServiceProvider.GetRequiredService<WorkerJobDispatcher>();
            IDurableQueueStore queueStore = processingScope.ServiceProvider.GetRequiredService<IDurableQueueStore>();

            executionResult = await dispatcher.DispatchAsync(claimedJob.Job, trace, cancellationToken);

            persistedJob = executionResult.Disposition switch
            {
                WorkerJobDisposition.Completed => await queueStore.CompleteAsync(
                    new CompleteQueueJobRequest(
                        QueueJobId: claimedJob.Job.QueueJobId,
                        LeaseOwner: _leaseOwner,
                        CompletedUtc: timeProvider.GetUtcNow()),
                    cancellationToken),
                WorkerJobDisposition.Rescheduled => await queueStore.RescheduleAsync(
                    new RescheduleQueueJobRequest(
                        QueueJobId: claimedJob.Job.QueueJobId,
                        LeaseOwner: _leaseOwner,
                        FailedUtc: timeProvider.GetUtcNow(),
                        NextAttemptUtc: executionResult.NextAttemptUtc ?? timeProvider.GetUtcNow().AddMilliseconds(Math.Max(queueOptions.Value.PollIntervalMilliseconds, 250)),
                        Error: executionResult.ErrorCode ?? "worker_rescheduled",
                        UpdatedPayloadJson: executionResult.UpdatedPayloadJson),
                    cancellationToken),
                WorkerJobDisposition.Failed => await queueStore.FailAsync(
                    new FailQueueJobRequest(
                        QueueJobId: claimedJob.Job.QueueJobId,
                        LeaseOwner: _leaseOwner,
                        FailedUtc: timeProvider.GetUtcNow(),
                        Error: executionResult.ErrorCode ?? "worker_failed"),
                    cancellationToken),
                _ => throw new InvalidOperationException($"Unsupported worker disposition '{executionResult.Disposition}'.")
            };
        }
        catch (Exception exception)
        {
            trace.SetErrorCode("worker_unhandled_exception");
            trace.AddNote(exception.GetType().Name);
            trace.AddNote("Worker hit an unhandled exception while processing the queue job.");

            using IServiceScope failureScope = scopeFactory.CreateScope();
            IDurableQueueStore queueStore = failureScope.ServiceProvider.GetRequiredService<IDurableQueueStore>();
            persistedJob = await queueStore.FailAsync(
                new FailQueueJobRequest(
                    QueueJobId: claimedJob.Job.QueueJobId,
                    LeaseOwner: _leaseOwner,
                    FailedUtc: timeProvider.GetUtcNow(),
                    Error: "worker_unhandled_exception"),
                cancellationToken);

            executionResult = new WorkerJobExecutionResult(
                WorkerJobDisposition.Failed,
                false,
                $"worker-job-{claimedJob.Job.QueueJobId}",
                "worker_unhandled_exception",
                exception.Message);
        }

        DateTimeOffset executionEndUtc = timeProvider.GetUtcNow();
        long executionEndTimestamp = timeProvider.GetTimestamp();

        JobTraceRecord record = new()
        {
            RunId = executionResult.RunId,
            TraceId = Guid.NewGuid().ToString("N"),
            JobId = claimedJob.Job.QueueJobId,
            JobType = claimedJob.Job.JobType,
            Region = layout.CurrentRegion,
            Service = layout.ServiceName,
            Status = persistedJob.Status,
            EnqueuedUtc = claimedJob.Job.EnqueuedUtc,
            DequeuedUtc = dequeuedUtc,
            ExecutionStartUtc = executionStartUtc,
            ExecutionEndUtc = executionEndUtc,
            QueueDelayMs = claimedJob.QueueDelayMs,
            ExecutionMs = timeProvider.GetElapsedTime(executionStartTimestamp, executionEndTimestamp).TotalMilliseconds,
            RetryCount = persistedJob.RetryCount,
            ContractSatisfied = executionResult.ContractSatisfied,
            DependencyCalls = trace.DependencyCalls.ToArray(),
            StageTimings = trace.StageTimings.ToArray(),
            ErrorCode = executionResult.ErrorCode ?? trace.ErrorCode,
            Notes = MergeNotes(trace.Notes, executionResult.ErrorDetail)
        };

        bool persisted = await jobTraceWriter.WriteAsync(record, cancellationToken);

        if (persisted)
        {
            logger.LogInformation(
                "Worker processed queue job {JobId} with status {Status} and wrote a job trace to {JobsPath}.",
                record.JobId,
                record.Status,
                layout.JobsJsonlPath);
        }
        else
        {
            logger.LogWarning(
                "Worker processed queue job {JobId} with status {Status}, but the job trace could not be persisted to {JobsPath}.",
                record.JobId,
                record.Status,
                layout.JobsJsonlPath);
        }
    }

    private static IReadOnlyList<string> MergeNotes(IReadOnlyList<string> notes, string? errorDetail)
    {
        if (string.IsNullOrWhiteSpace(errorDetail))
        {
            return notes;
        }

        return [.. notes, errorDetail.Trim()];
    }
}
