using System.Text.Json;
using Lab.Persistence;
using Lab.Persistence.Projections;
using Lab.Persistence.Queueing;
using Lab.Shared.Configuration;
using Lab.Shared.Queueing;

namespace Worker.Jobs;

internal sealed class OrderHistoryProjectionUpdateJobHandler(
    EnvironmentLayout layout,
    OrderHistoryProjectionRebuilder rebuilder) : IWorkerJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string JobType => LabQueueJobTypes.OrderHistoryProjectionUpdate;

    public async Task<WorkerJobExecutionResult> HandleAsync(
        QueueJobRecord job,
        WorkerJobTraceBuilder trace,
        CancellationToken cancellationToken)
    {
        OrderHistoryProjectionUpdateJobPayload payload;

        try
        {
            payload = JsonSerializer.Deserialize<OrderHistoryProjectionUpdateJobPayload>(job.PayloadJson, JsonOptions)
                ?? throw new InvalidOperationException("Queue job payload was empty.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            trace.SetErrorCode("invalid_order_history_projection_payload");
            return new WorkerJobExecutionResult(
                WorkerJobDisposition.Failed,
                false,
                $"worker-job-{job.QueueJobId}",
                "invalid_order_history_projection_payload",
                exception.Message);
        }

        string runId = string.IsNullOrWhiteSpace(payload.RunId) ? $"worker-job-{job.QueueJobId}" : payload.RunId.Trim();

        using WorkerJobTraceBuilder.StageScope stage = trace.BeginStage(
            "order_history_projection_update",
            new Dictionary<string, string?>
            {
                ["orderId"] = payload.OrderId,
                ["userId"] = payload.UserId
            });

        OrderHistoryProjectionRebuildResult result = await rebuilder.UpdateAsync(
            layout.PrimaryDatabasePath,
            layout.ReadModelDatabasePath,
            payload.OrderId,
            payload.UserId,
            cancellationToken);

        stage.Complete(
            result.ProjectionRowWritten ? "updated" : "removed",
            new Dictionary<string, string?>
            {
                ["rowsWritten"] = result.RowsWritten.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["projectedUtc"] = result.ProjectedUtc.ToString("O")
            });
        trace.AddNote(
            result.ProjectionRowWritten
                ? "Worker refreshed the order-history projection row from authoritative order tables."
                : "Worker removed or skipped the order-history projection row because the authoritative order no longer exists.");

        return new WorkerJobExecutionResult(
            WorkerJobDisposition.Completed,
            true,
            runId);
    }
}
