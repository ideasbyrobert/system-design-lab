using System.Text.Json;
using Lab.Persistence.Projections;
using Lab.Persistence.Queueing;
using Lab.Shared.Configuration;
using Lab.Shared.Queueing;

namespace Worker.Jobs;

internal sealed class ProductPageProjectionRebuildJobHandler(
    EnvironmentLayout layout,
    ProductPageProjectionRebuilder projectionRebuilder) : IWorkerJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string JobType => LabQueueJobTypes.ProductPageProjectionRebuild;

    public async Task<WorkerJobExecutionResult> HandleAsync(
        QueueJobRecord job,
        WorkerJobTraceBuilder trace,
        CancellationToken cancellationToken)
    {
        ProductPageProjectionRebuildJobPayload payload;

        try
        {
            payload = JsonSerializer.Deserialize<ProductPageProjectionRebuildJobPayload>(job.PayloadJson, JsonOptions)
                ?? new ProductPageProjectionRebuildJobPayload();
        }
        catch (JsonException exception)
        {
            trace.SetErrorCode("invalid_product_projection_payload");
            return new WorkerJobExecutionResult(
                WorkerJobDisposition.Failed,
                false,
                $"worker-job-{job.QueueJobId}",
                "invalid_product_projection_payload",
                exception.Message);
        }

        string runId = string.IsNullOrWhiteSpace(payload.RunId) ? $"worker-job-{job.QueueJobId}" : payload.RunId.Trim();
        string region = string.IsNullOrWhiteSpace(payload.Region) ? layout.CurrentRegion : payload.Region.Trim();

        using WorkerJobTraceBuilder.StageScope stage = trace.BeginStage(
            "product_page_projection_rebuilt",
            new Dictionary<string, string?>
            {
                ["region"] = region
            });

        ProductPageProjectionRebuildResult result = await projectionRebuilder.RebuildAsync(
            layout.PrimaryDatabasePath,
            layout.ReadModelDatabasePath,
            region,
            cancellationToken);

        stage.Complete(
            "rebuilt",
            new Dictionary<string, string?>
            {
                ["rowsWritten"] = result.RowsWritten.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["projectedUtc"] = result.ProjectedUtc.ToString("O")
            });

        trace.AddNote($"Product page projection rebuild wrote {result.RowsWritten} rows for region '{region}'.");

        return new WorkerJobExecutionResult(
            WorkerJobDisposition.Completed,
            true,
            runId);
    }
}
