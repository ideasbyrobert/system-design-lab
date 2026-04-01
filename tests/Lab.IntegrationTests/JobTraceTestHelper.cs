using System.Text.Json;
using Lab.Telemetry.RequestTracing;

namespace Lab.IntegrationTests;

internal static class JobTraceTestHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public sealed record TraceEnvelope(string Json, JobTraceRecord Record);

    public static async Task<IReadOnlyList<TraceEnvelope>> ReadJobTracesAsync(string repositoryRoot)
    {
        string jobsPath = Path.Combine(repositoryRoot, "logs", "jobs.jsonl");
        Assert.IsTrue(File.Exists(jobsPath), $"Expected job trace file at '{jobsPath}'.");

        string[] lines = await File.ReadAllLinesAsync(jobsPath);

        return lines
            .Select(line => new TraceEnvelope(
                line,
                JsonSerializer.Deserialize<JobTraceRecord>(line, JsonOptions)
                    ?? throw new InvalidOperationException("Job trace JSON could not be deserialized.")))
            .ToArray();
    }
}
