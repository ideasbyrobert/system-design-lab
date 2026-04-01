using System.Text;
using System.Text.Json;
using Lab.Analysis.Models;

namespace Lab.Analysis.Services;

public sealed class AnalysisArtifactWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task WriteSummaryJsonAsync(
        AnalysisSummary summary,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        EnsureParentDirectory(path);

        string payload = JsonSerializer.Serialize(summary, JsonOptions);
        await File.WriteAllTextAsync(path, payload, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteMarkdownReportAsync(
        AnalysisSummary summary,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        EnsureParentDirectory(path);

        string payload = BuildMarkdownReport(summary);
        await File.WriteAllTextAsync(path, payload, cancellationToken).ConfigureAwait(false);
    }

    public string BuildMarkdownReport(AnalysisSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        StringBuilder builder = new();

        builder.AppendLine($"# Analysis Report: {summary.RunId}");
        builder.AppendLine();
        builder.AppendLine($"Generated: `{summary.GeneratedUtc:O}`");
        builder.AppendLine();
        builder.AppendLine("## Contract");
        builder.AppendLine();
        builder.AppendLine($"This analyzer report covers the selected trace set for run filter `{summary.FilterRunId ?? summary.RunId}` and operation filter `{summary.FilterOperation ?? "all"}`.");
        builder.AppendLine();
        builder.AppendLine("It does not invent extra business promises beyond the trace data. It describes the behavior that actually crossed the selected observed boundary.");
        builder.AppendLine();

        builder.AppendLine("## Observation Boundary");
        builder.AppendLine();
        builder.AppendLine("The request boundary in this report is reconstructed from request trace lifetimes:");
        builder.AppendLine();
        builder.AppendLine("- start: request arrival recorded in the selected trace");
        builder.AppendLine("- end: request completion recorded in the selected trace");
        builder.AppendLine("- average concurrency: reconstructed from overlap of full observed lifetimes, not from thread counts");
        builder.AppendLine();
        builder.AppendLine("Processed job metrics are reported separately from job traces, and any live queue snapshot is a point-in-time view rather than a replay of the whole window.");
        builder.AppendLine();

        builder.AppendLine("## Topology");
        builder.AppendLine();
        builder.AppendLine("- Requests file: " + $"`{summary.RequestsPath}`");
        builder.AppendLine("- Jobs file: " + $"`{summary.JobsPath}`");
        builder.AppendLine("- Included run ids: " + $"`{FormatList(summary.IncludedRunIds)}`");
        builder.AppendLine("- Live queue snapshot captured: " + $"`{(summary.Queue.Captured ? "yes" : "no")}`");
        builder.AppendLine("- Read freshness data present: " + $"`{(summary.ReadFreshness.ReadRequestCount > 0 ? "yes" : "no")}`");
        builder.AppendLine("- Processed job traces present: " + $"`{(summary.Jobs.JobCount > 0 ? "yes" : "no")}`");
        builder.AppendLine();
        builder.AppendLine("Detailed process counts, regions, and dependency layouts must come from the surrounding experiment docs; the analyzer only reports what the selected traces and queue snapshot contain.");
        builder.AppendLine();

        builder.AppendLine("## Workload");
        builder.AppendLine();
        builder.AppendLine("- Filter run id: " + $"`{summary.FilterRunId ?? "all"}`");
        builder.AppendLine("- Filter from: " + $"`{FormatUtc(summary.FilterFromUtc)}`");
        builder.AppendLine("- Filter to: " + $"`{FormatUtc(summary.FilterToUtc)}`");
        builder.AppendLine("- Filter operation: " + $"`{summary.FilterOperation ?? "all"}`");
        builder.AppendLine("- Request count selected: " + $"`{summary.Requests.RequestCount}`");
        builder.AppendLine("- Completed requests in window: " + $"`{summary.Requests.CompletedRequestCount}`");
        builder.AppendLine("- Job count selected: " + $"`{summary.Jobs.JobCount}`");
        builder.AppendLine();
        builder.AppendLine("If you need the intended offered load, endpoint path, or scenario setup, pair this auto-generated report with the milestone experiment docs.");
        builder.AppendLine();

        builder.AppendLine("## Results");
        builder.AppendLine();
        builder.AppendLine("### Queue State Snapshot");
        builder.AppendLine();

        if (!summary.Queue.Captured)
        {
            builder.AppendLine($"No live queue snapshot was captured. Reason: `{summary.Queue.CaptureReason ?? "unknown"}`");
            builder.AppendLine();
        }
        else
        {
            builder.AppendLine("| Metric | Value |");
            builder.AppendLine("| --- | --- |");
            builder.AppendLine($"| Snapshot time | `{FormatUtc(summary.Queue.SnapshotUtc)}` |");
            builder.AppendLine($"| Queue run-id filter | `{summary.Queue.FilterRunId ?? "all"}` |");
            builder.AppendLine($"| Pending count | {summary.Queue.PendingCount} |");
            builder.AppendLine($"| Ready count | {summary.Queue.ReadyCount} |");
            builder.AppendLine($"| Delayed count | {summary.Queue.DelayedCount} |");
            builder.AppendLine($"| In-progress count | {summary.Queue.InProgressCount} |");
            builder.AppendLine($"| Completed count | {summary.Queue.CompletedCount} |");
            builder.AppendLine($"| Failed count | {summary.Queue.FailedCount} |");
            builder.AppendLine($"| Oldest queued enqueued UTC | `{FormatUtc(summary.Queue.OldestQueuedEnqueuedUtc)}` |");
            builder.AppendLine($"| Oldest queued item age (ms) | {FormatNumber(summary.Queue.OldestQueuedAgeMs)} |");
            builder.AppendLine();
        }

        builder.AppendLine("### Request Metrics");
        builder.AppendLine();
        builder.AppendLine("| Metric | Value |");
        builder.AppendLine("| --- | --- |");
        builder.AppendLine($"| Request count | {summary.Requests.RequestCount} |");
        builder.AppendLine($"| Completed in window | {summary.Requests.CompletedRequestCount} |");
        builder.AppendLine($"| Window start | `{FormatUtc(summary.Requests.WindowStartUtc)}` |");
        builder.AppendLine($"| Window end | `{FormatUtc(summary.Requests.WindowEndUtc)}` |");
        builder.AppendLine($"| Window duration (ms) | {FormatNumber(summary.Requests.WindowDurationMs)} |");
        builder.AppendLine($"| Average latency (ms) | {FormatNumber(summary.Requests.AverageLatencyMs)} |");
        builder.AppendLine($"| P50 latency (ms) | {FormatNumber(summary.Requests.P50LatencyMs)} |");
        builder.AppendLine($"| P95 latency (ms) | {FormatNumber(summary.Requests.P95LatencyMs)} |");
        builder.AppendLine($"| P99 latency (ms) | {FormatNumber(summary.Requests.P99LatencyMs)} |");
        builder.AppendLine($"| Throughput (req/s) | {FormatNumber(summary.Requests.ThroughputPerSecond)} |");
        builder.AppendLine($"| Average concurrency | {FormatNumber(summary.Requests.AverageConcurrency)} |");
        builder.AppendLine($"| Rate-limited fraction | {FormatPercent(summary.Requests.RateLimitedFraction)} |");
        builder.AppendLine($"| Cache hit rate | {FormatPercent(summary.Requests.CacheHitRate)} |");
        builder.AppendLine($"| Cache miss rate | {FormatPercent(summary.Requests.CacheMissRate)} |");
        builder.AppendLine($"| Errors by status | `{FormatDictionary(summary.Requests.ErrorCounts)}` |");
        builder.AppendLine();

        builder.AppendLine("### Read Freshness Metrics");
        builder.AppendLine();
        builder.AppendLine("| Metric | Value |");
        builder.AppendLine("| --- | --- |");
        builder.AppendLine($"| Read requests with freshness data | {summary.ReadFreshness.ReadRequestCount} |");
        builder.AppendLine($"| Stale request count | {summary.ReadFreshness.StaleRequestCount} |");
        builder.AppendLine($"| Stale request fraction | {FormatPercent(summary.ReadFreshness.StaleRequestFraction)} |");
        builder.AppendLine($"| Compared results | {summary.ReadFreshness.ComparedResultCount} |");
        builder.AppendLine($"| Stale results | {summary.ReadFreshness.StaleResultCount} |");
        builder.AppendLine($"| Stale result fraction | {FormatPercent(summary.ReadFreshness.StaleResultFraction)} |");
        builder.AppendLine($"| Average max staleness age (ms) | {FormatNumber(summary.ReadFreshness.AverageMaxStalenessAgeMs)} |");
        builder.AppendLine($"| Max observed staleness age (ms) | {FormatNumber(summary.ReadFreshness.MaxObservedStalenessAgeMs)} |");
        builder.AppendLine();

        if (summary.ReadFreshness.Sources.Count > 0)
        {
            builder.AppendLine("| Read source | Requests | Stale requests | Stale fraction | Avg latency (ms) | P95 latency (ms) | Avg max staleness age (ms) | Max staleness age (ms) |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");

            foreach (ReadSourceFreshnessMetricsSummary source in summary.ReadFreshness.Sources)
            {
                builder.AppendLine(
                    $"| `{source.ReadSource}` | {source.RequestCount} | {source.StaleRequestCount} | {FormatPercent(source.StaleRequestFraction)} | {FormatNumber(source.AverageLatencyMs)} | {FormatNumber(source.P95LatencyMs)} | {FormatNumber(source.AverageMaxStalenessAgeMs)} | {FormatNumber(source.MaxObservedStalenessAgeMs)} |");
            }

            builder.AppendLine();
        }

        builder.AppendLine("### Overload Metrics");
        builder.AppendLine();
        builder.AppendLine("| Metric | Value |");
        builder.AppendLine("| --- | --- |");
        builder.AppendLine($"| Rejected request count | {summary.Overload.RejectedRequestCount} |");
        builder.AppendLine($"| Reject fraction | {FormatPercent(summary.Overload.RejectFraction)} |");
        builder.AppendLine($"| Timeout request count | {summary.Overload.TimeoutRequestCount} |");
        builder.AppendLine($"| Timeout fraction | {FormatPercent(summary.Overload.TimeoutFraction)} |");
        builder.AppendLine($"| Admitted request count | {summary.Overload.AdmittedRequestCount} |");
        builder.AppendLine($"| Admitted fraction | {FormatPercent(summary.Overload.AdmittedFraction)} |");
        builder.AppendLine($"| Retried job count | {summary.Overload.RetriedJobCount} |");
        builder.AppendLine($"| Total retry attempts | {summary.Overload.TotalRetryAttempts} |");
        builder.AppendLine();

        builder.AppendLine("### Rate-Limited Requests");
        builder.AppendLine();
        AppendRequestCohortTable(builder, summary.Overload.RateLimitedRequests);
        builder.AppendLine();

        builder.AppendLine("### Admitted Requests");
        builder.AppendLine();
        AppendRequestCohortTable(builder, summary.Overload.AdmittedRequests);
        builder.AppendLine();

        builder.AppendLine("### Processed Job Metrics");
        builder.AppendLine();
        builder.AppendLine("| Metric | Value |");
        builder.AppendLine("| --- | --- |");
        builder.AppendLine($"| Job count | {summary.Jobs.JobCount} |");
        builder.AppendLine($"| Average queue delay (ms) | {FormatNumber(summary.Jobs.AverageQueueDelayMs)} |");
        builder.AppendLine($"| P95 queue delay (ms) | {FormatNumber(summary.Jobs.P95QueueDelayMs)} |");
        builder.AppendLine($"| Average execution (ms) | {FormatNumber(summary.Jobs.AverageExecutionMs)} |");
        builder.AppendLine($"| P95 execution (ms) | {FormatNumber(summary.Jobs.P95ExecutionMs)} |");
        builder.AppendLine($"| Retry distribution | `{FormatDictionary(summary.Jobs.RetryCountDistribution)}` |");
        builder.AppendLine();

        builder.AppendLine("## Interpretation");
        builder.AppendLine();
        builder.AppendLine(BuildInterpretation(summary));
        builder.AppendLine();

        builder.AppendLine("## Architectural Justification");
        builder.AppendLine();
        builder.AppendLine(BuildArchitecturalJustification(summary));

        return builder.ToString();
    }

    private static string BuildInterpretation(AnalysisSummary summary)
    {
        List<string> lines = [];

        if (summary.Requests.RequestCount == 0)
        {
            lines.Add("No request traces matched the selected filter, so the request metrics are empty rather than guessed.");
        }
        else
        {
            lines.Add(
                $"The request window carried `{FormatNumber(summary.Requests.AverageConcurrency)}` requests in flight on average, reconstructed exactly from observed lifetimes instead of thread counts.");

            if (summary.Requests.P50LatencyMs.HasValue && summary.Requests.P99LatencyMs.HasValue)
            {
                double p50 = summary.Requests.P50LatencyMs.Value;
                double p99 = summary.Requests.P99LatencyMs.Value;

                if (p50 > 0d && p99 / p50 >= 2d)
                {
                    lines.Add("The latency tail is much wider than the median, which means the average alone would hide meaningful slowdown risk.");
                }
                else
                {
                    lines.Add("The latency tail is relatively close to the median, so the selected window looks fairly tight.");
                }
            }

            if (summary.Requests.CacheHitRate.HasValue)
            {
                lines.Add($"Cache hits covered `{FormatPercent(summary.Requests.CacheHitRate)}` of the selected requests.");
            }

            if (summary.ReadFreshness.ReadRequestCount > 0)
            {
                lines.Add(
                    $"Freshness was evaluated on `{summary.ReadFreshness.ReadRequestCount}` read requests, with `{summary.ReadFreshness.StaleRequestCount}` stale responses (`{FormatPercent(summary.ReadFreshness.StaleRequestFraction)}`) and a max observed lag of `{FormatNumber(summary.ReadFreshness.MaxObservedStalenessAgeMs)}` ms.");
            }

            if (summary.Overload.RejectedRequestCount > 0)
            {
                lines.Add(
                    $"The overload split shows `{summary.Overload.RejectedRequestCount}` rejected requests (`{FormatPercent(summary.Overload.RejectFraction)}`), so protection is actively shedding work instead of letting every request join the slow path.");
            }
            else
            {
                lines.Add("No selected requests were rejected, so any overload signal has to show up as slower admitted work rather than explicit shedding.");
            }

            if (summary.Overload.TimeoutRequestCount > 0)
            {
                lines.Add(
                    $"`{summary.Overload.TimeoutRequestCount}` admitted requests still timed out, which means protection did not eliminate slow-path failure inside the admitted set.");
            }
        }

        if (!summary.Queue.Captured)
        {
            lines.Add("No live queue snapshot was available, so current backlog counts and oldest queued age could not be reported.");
        }
        else if (summary.Queue.PendingCount > 0)
        {
            lines.Add(
                $"The live queue snapshot shows `{summary.Queue.PendingCount}` pending jobs, `{summary.Queue.InProgressCount}` in progress, and an oldest queued age of `{FormatNumber(summary.Queue.OldestQueuedAgeMs)}` ms.");
        }
        else
        {
            lines.Add("The live queue snapshot shows no pending jobs at the analysis moment, so no current backlog is visible.");
        }

        if (summary.Jobs.JobCount == 0)
        {
            lines.Add("No job traces matched the selected filter, so waiting-versus-execution decomposition is unavailable even if backlog exists.");
        }
        else if ((summary.Jobs.AverageQueueDelayMs ?? 0d) > (summary.Jobs.AverageExecutionMs ?? 0d))
        {
            lines.Add("Average queue delay is larger than average execution time, so waiting dominates work in the current worker sample.");
        }
        else
        {
            lines.Add("Average execution time is at least as large as average queue delay, so the current worker sample is more execution-bound than queue-bound.");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    private static string BuildArchitecturalJustification(AnalysisSummary summary)
    {
        List<string> lines = [];

        lines.Add("Architectural discussion should stay attached to the measured dominant term rather than to a slogan or a preferred pattern.");

        if (summary.Requests.RequestCount == 0)
        {
            lines.Add("This report cannot justify any boundary-level architectural change because no request traces matched the selected filter.");
            return string.Join(Environment.NewLine + Environment.NewLine, lines);
        }

        if (summary.Requests.P50LatencyMs.HasValue && summary.Requests.P95LatencyMs.HasValue)
        {
            double p50 = summary.Requests.P50LatencyMs.Value;
            double p95 = summary.Requests.P95LatencyMs.Value;

            if (p50 > 0d && p95 / p50 >= 1.5d)
            {
                lines.Add("The wide `P95` relative to `P50` means tail behavior is materially different from the median, so any architecture choice justified by this run should preserve percentiles and not rely on averages alone.");
            }
        }

        if (summary.Requests.CacheHitRate.GetValueOrDefault() >= 0.5d)
        {
            lines.Add($"Cache hits covered `{FormatPercent(summary.Requests.CacheHitRate)}` of selected requests, so the cache is now a real part of the boundary behavior rather than an implementation detail.");
        }

        if (summary.ReadFreshness.ReadRequestCount > 0)
        {
            lines.Add($"Freshness data is present and shows `{summary.ReadFreshness.StaleRequestCount}` stale requests out of `{summary.ReadFreshness.ReadRequestCount}`, so any replica or read-model justification must explicitly include the observed freshness window.");
        }

        if (summary.Overload.RejectedRequestCount > 0)
        {
            lines.Add($"The run rejected `{summary.Overload.RejectedRequestCount}` requests (`{FormatPercent(summary.Overload.RejectFraction)}`), which justifies describing the system as using admission control or boundary protection rather than calling it simply “faster.”");
        }

        if (summary.Queue.Captured && summary.Queue.PendingCount > 0)
        {
            lines.Add($"The live queue snapshot shows `{summary.Queue.PendingCount}` pending jobs with oldest queued age `{FormatNumber(summary.Queue.OldestQueuedAgeMs)}` ms, so background work is part of the current system cost even if it is off the request path.");
        }

        if ((summary.Jobs.AverageQueueDelayMs ?? 0d) > (summary.Jobs.AverageExecutionMs ?? 0d) && summary.Jobs.JobCount > 0)
        {
            lines.Add("Average queue delay exceeds average job execution time, so the current background path is more waiting-bound than handler-bound.");
        }
        else if (summary.Jobs.JobCount > 0)
        {
            lines.Add("Average execution time is at least as large as average queue delay, so the current background path is more execution-bound than queue-bound.");
        }

        lines.Add("The safe architectural conclusion is therefore the narrow one supported by the measured boundary, queue state, and freshness data, not a broader claim that this mechanism is universally better.");

        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    private static void EnsureParentDirectory(string path)
    {
        string? directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string FormatList(IReadOnlyList<string> values) =>
        values.Count == 0 ? "none" : string.Join(", ", values);

    private static string FormatDictionary(IReadOnlyDictionary<string, int> values) =>
        values.Count == 0
            ? "none"
            : string.Join(", ", values.Select(pair => $"{pair.Key}={pair.Value}"));

    private static string FormatUtc(DateTimeOffset? value) =>
        value.HasValue ? value.Value.ToString("O") : "n/a";

    private static string FormatNumber(double? value) =>
        value.HasValue ? value.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "n/a";

    private static string FormatPercent(double? value) =>
        value.HasValue
            ? (value.Value * 100d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%"
            : "n/a";

    private static void AppendRequestCohortTable(StringBuilder builder, RequestCohortMetricsSummary summary)
    {
        builder.AppendLine("| Metric | Value |");
        builder.AppendLine("| --- | --- |");
        builder.AppendLine($"| Request count | {summary.RequestCount} |");
        builder.AppendLine($"| Completed in window | {summary.CompletedRequestCount} |");
        builder.AppendLine($"| Fraction of selected requests | {FormatPercent(summary.Fraction)} |");
        builder.AppendLine($"| Average latency (ms) | {FormatNumber(summary.AverageLatencyMs)} |");
        builder.AppendLine($"| P95 latency (ms) | {FormatNumber(summary.P95LatencyMs)} |");
        builder.AppendLine($"| Throughput (req/s) | {FormatNumber(summary.ThroughputPerSecond)} |");
        builder.AppendLine($"| Average concurrency | {FormatNumber(summary.AverageConcurrency)} |");
        builder.AppendLine($"| Errors by status | `{FormatDictionary(summary.ErrorCounts)}` |");
    }
}
