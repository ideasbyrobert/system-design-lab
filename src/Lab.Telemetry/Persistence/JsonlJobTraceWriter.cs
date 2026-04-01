using System.Text.Json;
using Lab.Shared.IO;
using Lab.Telemetry.RequestTracing;
using Microsoft.Extensions.Logging;

namespace Lab.Telemetry.Persistence;

public sealed class JsonlJobTraceWriter(
    string path,
    SafeFileAppender fileAppender,
    ILogger<JsonlJobTraceWriter> logger) : IJobTraceWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<bool> WriteAsync(
        JobTraceRecord traceRecord,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(traceRecord);

        try
        {
            string json = JsonSerializer.Serialize(traceRecord, JsonOptions);
            await fileAppender.AppendLineAsync(path, json, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to persist job trace {JobId} for job type {JobType} to {Path}.",
                traceRecord.JobId,
                traceRecord.JobType,
                path);

            return false;
        }
    }
}
