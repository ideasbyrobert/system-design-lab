using System.Diagnostics;
using LoadGenTool.Cli;
using LoadGenTool.Workloads;
using Lab.Shared.Configuration;
using Lab.Shared.Http;
using Lab.Shared.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (!LoadGenOptions.TryParse(args, out LoadGenOptions options, out string? error))
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine();
    Console.Error.WriteLine(LoadGenOptions.GetUsage());
    Environment.ExitCode = 1;
    return;
}

if (options.ShowHelp)
{
    Console.WriteLine(LoadGenOptions.GetUsage());
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddLabConfiguration(builder.Configuration, builder.Environment);
builder.Logging.AddLabOperationalFileLogging();

using var host = builder.Build();
host.LogResolvedLabEnvironment();

ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("LoadGen");

byte[]? payloadBytes = null;

if (!string.IsNullOrWhiteSpace(options.PayloadFile))
{
    payloadBytes = await File.ReadAllBytesAsync(options.PayloadFile);
}

IReadOnlyList<TimeSpan> offsets = LoadSchedulePlanner.CreateOffsets(options);
SemaphoreSlim concurrencyGate = new(options.ConcurrencyCap, options.ConcurrencyCap);
List<LoadRequestResult> results = [];
List<Task> inFlight = [];
Stopwatch stopwatch = Stopwatch.StartNew();
int requestSequence = 0;

using HttpClient client = new();

foreach (TimeSpan offset in offsets)
{
    TimeSpan delay = offset - stopwatch.Elapsed;

    if (delay > TimeSpan.Zero)
    {
        await Task.Delay(delay);
    }

    await concurrencyGate.WaitAsync();
    int sequence = Interlocked.Increment(ref requestSequence);

    inFlight.Add(Task.Run(async () =>
    {
        try
        {
            LoadRequestResult result = await SendRequestAsync(
                client,
                options,
                payloadBytes,
                sequence);

            lock (results)
            {
                results.Add(result);
            }
        }
        finally
        {
            concurrencyGate.Release();
        }
    }));
}

await Task.WhenAll(inFlight);

LoadRunSummary summary = BuildSummary(options, results, offsets.Count);

logger.LogInformation(
    "Completed load run {RunId}. Planned={Planned}, Completed={Completed}, Success={Success}, Failure={Failure}, AvgLatencyMs={AverageLatencyMs}, P95LatencyMs={P95LatencyMs}.",
    summary.RunId,
    summary.PlannedRequestCount,
    summary.CompletedRequestCount,
    summary.SuccessCount,
    summary.FailureCount,
    summary.AverageLatencyMs,
    summary.P95LatencyMs);

Console.WriteLine($"RunId: {summary.RunId}");
Console.WriteLine($"Mode: {summary.Mode}");
Console.WriteLine($"Target: {summary.TargetUrl}");
Console.WriteLine($"Planned requests: {summary.PlannedRequestCount}");
Console.WriteLine($"Completed requests: {summary.CompletedRequestCount}");
Console.WriteLine($"Successes: {summary.SuccessCount}");
Console.WriteLine($"Failures: {summary.FailureCount}");
Console.WriteLine($"Average latency (ms): {FormatNumber(summary.AverageLatencyMs)}");
Console.WriteLine($"P95 latency (ms): {FormatNumber(summary.P95LatencyMs)}");
Console.WriteLine($"Status counts: {FormatCounts(summary.StatusCounts)}");
Console.WriteLine($"Error counts: {FormatCounts(summary.ErrorCounts)}");

static async Task<LoadRequestResult> SendRequestAsync(
    HttpClient client,
    LoadGenOptions options,
    byte[]? payloadBytes,
    int sequence)
{
    string correlationId = $"{options.RunId}-{sequence:D6}";
    long startedTimestamp = Stopwatch.GetTimestamp();

    try
    {
        using HttpRequestMessage request = new(new HttpMethod(options.Method), options.TargetUrl);

        if (payloadBytes is not null)
        {
            request.Content = new ByteArrayContent(payloadBytes);
        }

        foreach ((string key, string value) in options.Headers)
        {
            if (!request.Headers.TryAddWithoutValidation(key, value) && request.Content is not null)
            {
                request.Content.Headers.TryAddWithoutValidation(key, value);
            }
        }

        request.Headers.Remove(LabHeaderNames.RunId);
        request.Headers.Remove(LabHeaderNames.CorrelationId);
        request.Headers.TryAddWithoutValidation(LabHeaderNames.RunId, options.RunId);
        request.Headers.TryAddWithoutValidation(LabHeaderNames.CorrelationId, correlationId);

        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        _ = await response.Content.ReadAsByteArrayAsync();

        double elapsedMs = Stopwatch.GetElapsedTime(startedTimestamp, Stopwatch.GetTimestamp()).TotalMilliseconds;

        return new LoadRequestResult(
            CorrelationId: correlationId,
            StatusCode: (int)response.StatusCode,
            ElapsedMs: elapsedMs,
            Succeeded: response.IsSuccessStatusCode,
            Error: null);
    }
    catch (Exception exception)
    {
        double elapsedMs = Stopwatch.GetElapsedTime(startedTimestamp, Stopwatch.GetTimestamp()).TotalMilliseconds;

        return new LoadRequestResult(
            CorrelationId: correlationId,
            StatusCode: null,
            ElapsedMs: elapsedMs,
            Succeeded: false,
            Error: exception.GetType().Name);
    }
}

static LoadRunSummary BuildSummary(
    LoadGenOptions options,
    IReadOnlyList<LoadRequestResult> results,
    int plannedRequestCount)
{
    double[] latencies = results.Select(result => result.ElapsedMs).OrderBy(value => value).ToArray();

    return new LoadRunSummary
    {
        RunId = options.RunId,
        TargetUrl = options.TargetUrl,
        Mode = options.Mode,
        PlannedRequestCount = plannedRequestCount,
        CompletedRequestCount = results.Count,
        SuccessCount = results.Count(result => result.Succeeded),
        FailureCount = results.Count(result => !result.Succeeded),
        AverageLatencyMs = latencies.Length > 0 ? latencies.Average() : null,
        P95LatencyMs = Percentile(latencies, 0.95d),
        StatusCounts = results
            .Where(result => result.StatusCode.HasValue)
            .GroupBy(result => result.StatusCode!.Value.ToString(), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
        ErrorCounts = results
            .Where(result => !string.IsNullOrWhiteSpace(result.Error))
            .GroupBy(result => result.Error!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal)
    };
}

static double? Percentile(IReadOnlyList<double> sortedValues, double percentile)
{
    if (sortedValues.Count == 0)
    {
        return null;
    }

    int index = Math.Clamp((int)Math.Ceiling(percentile * sortedValues.Count) - 1, 0, sortedValues.Count - 1);
    return sortedValues[index];
}

static string FormatNumber(double? value) =>
    value.HasValue
        ? value.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
        : "n/a";

static string FormatCounts(IReadOnlyDictionary<string, int> values) =>
    values.Count == 0
        ? "none"
        : string.Join(", ", values.Select(pair => $"{pair.Key}={pair.Value}"));
