using Lab.Persistence.Queueing;

namespace Worker.Jobs;

internal interface IWorkerJobHandler
{
    string JobType { get; }

    Task<WorkerJobExecutionResult> HandleAsync(
        QueueJobRecord job,
        WorkerJobTraceBuilder trace,
        CancellationToken cancellationToken);
}
