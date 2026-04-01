using System.Text.Json;
using Lab.Shared.IO;
using Lab.Telemetry.RequestTracing;
using Microsoft.Extensions.Logging;

namespace Lab.Telemetry.Persistence;

public sealed class JsonlRequestTraceWriter(
    string path,
    SafeFileAppender fileAppender,
    ILogger<JsonlRequestTraceWriter> logger) : IRequestTraceWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<bool> WriteAsync(
        RequestTraceRecord traceRecord,
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
                "Failed to persist request trace {TraceId} for operation {Operation} to {Path}.",
                traceRecord.TraceId,
                traceRecord.Operation,
                path);

            return false;
        }
    }
}
