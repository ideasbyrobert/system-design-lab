using Lab.Persistence.Queueing;

namespace Worker.Jobs;

internal sealed class WorkerJobDispatcher(IEnumerable<IWorkerJobHandler> handlers)
{
    private readonly IReadOnlyDictionary<string, IWorkerJobHandler> _handlers = handlers.ToDictionary(
        item => item.JobType,
        item => item,
        StringComparer.Ordinal);

    public Task<WorkerJobExecutionResult> DispatchAsync(
        QueueJobRecord job,
        WorkerJobTraceBuilder trace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(trace);

        if (_handlers.TryGetValue(job.JobType, out IWorkerJobHandler? handler))
        {
            return handler.HandleAsync(job, trace, cancellationToken);
        }

        trace.SetErrorCode("unknown_job_type");
        trace.AddNote($"Worker does not know how to process job type '{job.JobType}'.");
        trace.RecordInstantStage(
            "job_rejected",
            outcome: "unknown_job_type",
            metadata: new Dictionary<string, string?>
            {
                ["jobType"] = job.JobType
            });

        return Task.FromResult(
            new WorkerJobExecutionResult(
                Disposition: WorkerJobDisposition.Failed,
                ContractSatisfied: false,
                RunId: ResolveRunId(job),
                ErrorCode: "unknown_job_type",
                ErrorDetail: $"Job type '{job.JobType}' is not registered in Worker."));
    }

    private static string ResolveRunId(QueueJobRecord job)
    {
        if (!string.IsNullOrWhiteSpace(job.PayloadJson))
        {
            try
            {
                using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(job.PayloadJson);
                if (document.RootElement.TryGetProperty("runId", out System.Text.Json.JsonElement runIdElement) &&
                    runIdElement.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    string? value = runIdElement.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }
            }
            catch (System.Text.Json.JsonException)
            {
            }
        }

        return $"worker-job-{job.QueueJobId}";
    }
}
