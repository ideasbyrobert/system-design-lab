using System.Text.Json;
using Lab.Telemetry.RequestTracing;
using Microsoft.Extensions.Logging;

namespace Lab.Analysis.Services;

public sealed class TelemetryJsonlReader(ILogger<TelemetryJsonlReader> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<RequestTraceRecord>> ReadRequestTracesAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        return await ReadRecordsAsync<RequestTraceRecord>(path, "request", cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<JobTraceRecord>> ReadJobTracesAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        return await ReadRecordsAsync<JobTraceRecord>(path, "job", cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<TRecord>> ReadRecordsAsync<TRecord>(
        string path,
        string kind,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            logger.LogWarning("The {Kind} telemetry file {Path} does not exist. Treating it as empty.", kind, path);
            return Array.Empty<TRecord>();
        }

        List<TRecord> records = [];

        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using StreamReader reader = new(stream);

        int lineNumber = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                TRecord? record = JsonSerializer.Deserialize<TRecord>(line, JsonOptions);

                if (record is not null)
                {
                    records.Add(record);
                }
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException(
                    $"Could not parse {kind} telemetry from '{path}' at line {lineNumber}.",
                    exception);
            }
        }

        return records;
    }
}
