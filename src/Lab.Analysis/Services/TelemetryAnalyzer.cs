using Lab.Analysis.Models;
using Lab.Telemetry.RequestTracing;

namespace Lab.Analysis.Services;

public sealed class TelemetryAnalyzer(TelemetryJsonlReader reader, QueueMetricsReader queueMetricsReader, TimeProvider timeProvider)
{
    public async Task<AnalysisSummary> AnalyzeAsync(
        string requestsPath,
        string jobsPath,
        string? primaryDatabasePath = null,
        AnalysisFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        filter ??= AnalysisFilter.None;

        DateTimeOffset generatedUtc = timeProvider.GetUtcNow();

        IReadOnlyList<RequestTraceRecord> requestTraces = await reader
            .ReadRequestTracesAsync(requestsPath, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<JobTraceRecord> jobTraces = await reader
            .ReadJobTracesAsync(jobsPath, cancellationToken)
            .ConfigureAwait(false);

        QueueMetricsSummary queue = await queueMetricsReader
            .ReadAsync(primaryDatabasePath, generatedUtc, filter.RunId, cancellationToken)
            .ConfigureAwait(false);

        return Analyze(requestTraces, jobTraces, filter, requestsPath, jobsPath, generatedUtc, queue);
    }

    public AnalysisSummary Analyze(
        IEnumerable<RequestTraceRecord> requestTraces,
        IEnumerable<JobTraceRecord> jobTraces,
        AnalysisFilter? filter = null,
        string requestsPath = "",
        string jobsPath = "",
        DateTimeOffset? generatedUtc = null,
        QueueMetricsSummary? queue = null)
    {
        ArgumentNullException.ThrowIfNull(requestTraces);
        ArgumentNullException.ThrowIfNull(jobTraces);

        filter ??= AnalysisFilter.None;

        RequestTraceRecord[] selectedRequests = requestTraces.Where(filter.Matches).ToArray();
        JobTraceRecord[] selectedJobs = jobTraces.Where(filter.Matches).ToArray();

        string[] includedRunIds = selectedRequests.Select(trace => trace.RunId)
            .Concat(selectedJobs.Select(trace => trace.RunId))
            .Where(runId => !string.IsNullOrWhiteSpace(runId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(runId => runId, StringComparer.Ordinal)
            .ToArray();

        DateTimeOffset effectiveGeneratedUtc = generatedUtc ?? timeProvider.GetUtcNow();

        return new AnalysisSummary
        {
            RunId = ResolveOutputRunId(filter, includedRunIds, effectiveGeneratedUtc),
            GeneratedUtc = effectiveGeneratedUtc,
            RequestsPath = requestsPath,
            JobsPath = jobsPath,
            FilterRunId = filter.RunId,
            FilterFromUtc = filter.FromUtc,
            FilterToUtc = filter.ToUtc,
            FilterOperation = filter.Operation,
            IncludedRunIds = includedRunIds,
            Queue = queue ?? new QueueMetricsSummary(),
            Requests = ComputeRequestMetrics(selectedRequests, filter),
            ReadFreshness = ComputeReadFreshnessMetrics(selectedRequests),
            Overload = ComputeOverloadMetrics(selectedRequests, selectedJobs, filter),
            Jobs = ComputeJobMetrics(selectedJobs)
        };
    }

    private static RequestMetricsSummary ComputeRequestMetrics(
        IReadOnlyList<RequestTraceRecord> requestTraces,
        AnalysisFilter filter)
    {
        if (requestTraces.Count == 0)
        {
            return new RequestMetricsSummary();
        }

        DateTimeOffset windowStart = filter.FromUtc ?? requestTraces.Min(trace => trace.ArrivalUtc);
        DateTimeOffset windowEnd = filter.ToUtc ?? requestTraces.Max(trace => trace.CompletionUtc);
        double windowDurationMs = Math.Max(0d, (windowEnd - windowStart).TotalMilliseconds);

        double[] latencies = requestTraces
            .Select(trace => trace.LatencyMs)
            .OrderBy(latency => latency)
            .ToArray();

        int completedWithinWindowCount = requestTraces.Count(trace => trace.CompletionUtc >= windowStart && trace.CompletionUtc <= windowEnd);

        return new RequestMetricsSummary
        {
            RequestCount = requestTraces.Count,
            CompletedRequestCount = completedWithinWindowCount,
            WindowStartUtc = windowStart,
            WindowEndUtc = windowEnd,
            WindowDurationMs = windowDurationMs,
            AverageLatencyMs = latencies.Average(),
            P50LatencyMs = Percentile(latencies, 0.50d),
            P95LatencyMs = Percentile(latencies, 0.95d),
            P99LatencyMs = Percentile(latencies, 0.99d),
            ThroughputPerSecond = windowDurationMs > 0d ? completedWithinWindowCount / (windowDurationMs / 1000d) : null,
            AverageConcurrency = ComputeAverageConcurrency(requestTraces, windowStart, windowEnd),
            RateLimitedFraction = requestTraces.Average(trace => trace.RateLimited ? 1d : 0d),
            CacheHitRate = requestTraces.Average(trace => trace.CacheHit ? 1d : 0d),
            CacheMissRate = requestTraces.Average(trace => trace.CacheHit ? 0d : 1d),
            ErrorCounts = requestTraces
                .Where(trace => trace.StatusCode >= 400)
                .GroupBy(trace => trace.StatusCode.ToString(), StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal)
        };
    }

    private static JobMetricsSummary ComputeJobMetrics(IReadOnlyList<JobTraceRecord> jobTraces)
    {
        if (jobTraces.Count == 0)
        {
            return new JobMetricsSummary();
        }

        double[] queueDelays = jobTraces
            .Where(trace => trace.QueueDelayMs.HasValue)
            .Select(trace => trace.QueueDelayMs!.Value)
            .OrderBy(delay => delay)
            .ToArray();

        double[] executionTimes = jobTraces
            .Where(trace => trace.ExecutionMs.HasValue)
            .Select(trace => trace.ExecutionMs!.Value)
            .OrderBy(delay => delay)
            .ToArray();

        return new JobMetricsSummary
        {
            JobCount = jobTraces.Count,
            AverageQueueDelayMs = queueDelays.Length > 0 ? queueDelays.Average() : null,
            P95QueueDelayMs = Percentile(queueDelays, 0.95d),
            AverageExecutionMs = executionTimes.Length > 0 ? executionTimes.Average() : null,
            P95ExecutionMs = Percentile(executionTimes, 0.95d),
            RetryCountDistribution = jobTraces
                .GroupBy(trace => trace.RetryCount.ToString(), StringComparer.Ordinal)
                .OrderBy(group => int.Parse(group.Key, System.Globalization.CultureInfo.InvariantCulture))
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal)
        };
    }

    private static OverloadMetricsSummary ComputeOverloadMetrics(
        IReadOnlyList<RequestTraceRecord> requestTraces,
        IReadOnlyList<JobTraceRecord> jobTraces,
        AnalysisFilter filter)
    {
        DateTimeOffset? windowStart = requestTraces.Count > 0
            ? filter.FromUtc ?? requestTraces.Min(trace => trace.ArrivalUtc)
            : filter.FromUtc;
        DateTimeOffset? windowEnd = requestTraces.Count > 0
            ? filter.ToUtc ?? requestTraces.Max(trace => trace.CompletionUtc)
            : filter.ToUtc;

        RequestTraceRecord[] rejectedRequests = requestTraces
            .Where(IsRejectedRequest)
            .ToArray();
        RequestTraceRecord[] admittedRequests = requestTraces
            .Where(trace => !IsRejectedRequest(trace))
            .ToArray();

        int totalRetryAttempts = jobTraces.Sum(trace => Math.Max(0, trace.RetryCount));
        int retriedJobCount = jobTraces.Count(trace => trace.RetryCount > 0);
        int timeoutRequestCount = admittedRequests.Count(IsTimeoutRequest);

        return new OverloadMetricsSummary
        {
            RejectedRequestCount = rejectedRequests.Length,
            RejectFraction = requestTraces.Count > 0 ? rejectedRequests.Length / (double)requestTraces.Count : null,
            TimeoutRequestCount = timeoutRequestCount,
            TimeoutFraction = requestTraces.Count > 0 ? timeoutRequestCount / (double)requestTraces.Count : null,
            AdmittedRequestCount = admittedRequests.Length,
            AdmittedFraction = requestTraces.Count > 0 ? admittedRequests.Length / (double)requestTraces.Count : null,
            RetriedJobCount = retriedJobCount,
            TotalRetryAttempts = totalRetryAttempts,
            RateLimitedRequests = ComputeRequestCohortMetrics(rejectedRequests, requestTraces.Count, windowStart, windowEnd),
            AdmittedRequests = ComputeRequestCohortMetrics(admittedRequests, requestTraces.Count, windowStart, windowEnd)
        };
    }

    private static ReadFreshnessMetricsSummary ComputeReadFreshnessMetrics(
        IReadOnlyList<RequestTraceRecord> requestTraces)
    {
        RequestTraceRecord[] readTraces = requestTraces
            .Where(trace => !string.IsNullOrWhiteSpace(trace.ReadSource))
            .ToArray();

        if (readTraces.Length == 0)
        {
            return new ReadFreshnessMetricsSummary();
        }

        int staleRequestCount = readTraces.Count(trace => (trace.FreshnessStaleCount ?? 0) > 0);
        int comparedResultCount = readTraces.Sum(trace => trace.FreshnessComparedCount ?? 0);
        int staleResultCount = readTraces.Sum(trace => trace.FreshnessStaleCount ?? 0);
        double[] stalenessAges = readTraces
            .Where(trace => trace.MaxStalenessAgeMs.HasValue)
            .Select(trace => trace.MaxStalenessAgeMs!.Value)
            .OrderBy(age => age)
            .ToArray();

        ReadSourceFreshnessMetricsSummary[] sources = readTraces
            .GroupBy(trace => trace.ReadSource!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                RequestTraceRecord[] tracesForSource = group.ToArray();
                double[] latencies = tracesForSource
                    .Select(trace => trace.LatencyMs)
                    .OrderBy(latency => latency)
                    .ToArray();
                double[] sourceStalenessAges = tracesForSource
                    .Where(trace => trace.MaxStalenessAgeMs.HasValue)
                    .Select(trace => trace.MaxStalenessAgeMs!.Value)
                    .OrderBy(age => age)
                    .ToArray();
                int sourceStaleRequestCount = tracesForSource.Count(trace => (trace.FreshnessStaleCount ?? 0) > 0);

                return new ReadSourceFreshnessMetricsSummary
                {
                    ReadSource = group.Key,
                    RequestCount = tracesForSource.Length,
                    StaleRequestCount = sourceStaleRequestCount,
                    StaleRequestFraction = tracesForSource.Length > 0
                        ? sourceStaleRequestCount / (double)tracesForSource.Length
                        : null,
                    AverageLatencyMs = latencies.Length > 0 ? latencies.Average() : null,
                    P95LatencyMs = Percentile(latencies, 0.95d),
                    AverageMaxStalenessAgeMs = sourceStalenessAges.Length > 0 ? sourceStalenessAges.Average() : null,
                    MaxObservedStalenessAgeMs = sourceStalenessAges.Length > 0 ? sourceStalenessAges.Max() : null
                };
            })
            .ToArray();

        return new ReadFreshnessMetricsSummary
        {
            ReadRequestCount = readTraces.Length,
            StaleRequestCount = staleRequestCount,
            StaleRequestFraction = readTraces.Length > 0 ? staleRequestCount / (double)readTraces.Length : null,
            ComparedResultCount = comparedResultCount,
            StaleResultCount = staleResultCount,
            StaleResultFraction = comparedResultCount > 0 ? staleResultCount / (double)comparedResultCount : null,
            AverageMaxStalenessAgeMs = stalenessAges.Length > 0 ? stalenessAges.Average() : null,
            MaxObservedStalenessAgeMs = stalenessAges.Length > 0 ? stalenessAges.Max() : null,
            Sources = sources
        };
    }

    private static RequestCohortMetricsSummary ComputeRequestCohortMetrics(
        IReadOnlyList<RequestTraceRecord> requestTraces,
        int totalRequestCount,
        DateTimeOffset? windowStart,
        DateTimeOffset? windowEnd)
    {
        if (!windowStart.HasValue || !windowEnd.HasValue)
        {
            return new RequestCohortMetricsSummary
            {
                RequestCount = requestTraces.Count,
                CompletedRequestCount = requestTraces.Count,
                Fraction = totalRequestCount > 0 ? requestTraces.Count / (double)totalRequestCount : null,
                AverageLatencyMs = requestTraces.Count > 0 ? requestTraces.Average(trace => trace.LatencyMs) : null,
                P95LatencyMs = Percentile(
                    requestTraces.Select(trace => trace.LatencyMs).OrderBy(latency => latency).ToArray(),
                    0.95d),
                ErrorCounts = requestTraces
                    .Where(trace => trace.StatusCode >= 400)
                    .GroupBy(trace => trace.StatusCode.ToString(), StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal)
            };
        }

        double windowDurationMs = Math.Max(0d, (windowEnd.Value - windowStart.Value).TotalMilliseconds);
        double[] latencies = requestTraces
            .Select(trace => trace.LatencyMs)
            .OrderBy(latency => latency)
            .ToArray();
        int completedWithinWindowCount = requestTraces.Count(trace =>
            trace.CompletionUtc >= windowStart.Value &&
            trace.CompletionUtc <= windowEnd.Value);

        return new RequestCohortMetricsSummary
        {
            RequestCount = requestTraces.Count,
            CompletedRequestCount = completedWithinWindowCount,
            Fraction = totalRequestCount > 0 ? requestTraces.Count / (double)totalRequestCount : null,
            AverageLatencyMs = latencies.Length > 0 ? latencies.Average() : null,
            P95LatencyMs = Percentile(latencies, 0.95d),
            ThroughputPerSecond = windowDurationMs > 0d ? completedWithinWindowCount / (windowDurationMs / 1000d) : null,
            AverageConcurrency = windowDurationMs > 0d
                ? ComputeAverageConcurrency(requestTraces, windowStart.Value, windowEnd.Value) ?? 0d
                : null,
            ErrorCounts = requestTraces
                .Where(trace => trace.StatusCode >= 400)
                .GroupBy(trace => trace.StatusCode.ToString(), StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal)
        };
    }

    private static string ResolveOutputRunId(
        AnalysisFilter filter,
        IReadOnlyList<string> includedRunIds,
        DateTimeOffset generatedUtc)
    {
        if (!string.IsNullOrWhiteSpace(filter.RunId))
        {
            return filter.RunId;
        }

        if (includedRunIds.Count == 1)
        {
            return includedRunIds[0];
        }

        return $"analysis-{generatedUtc:yyyyMMddTHHmmssfffZ}";
    }

    private static double? Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return null;
        }

        int index = Math.Clamp((int)Math.Ceiling(percentile * sortedValues.Count) - 1, 0, sortedValues.Count - 1);
        return sortedValues[index];
    }

    private static double? ComputeAverageConcurrency(
        IReadOnlyList<RequestTraceRecord> requestTraces,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        double windowDurationMs = (windowEnd - windowStart).TotalMilliseconds;

        if (windowDurationMs <= 0d)
        {
            return null;
        }

        double accumulatedLifetimeMs = 0d;

        foreach (RequestTraceRecord trace in requestTraces)
        {
            DateTimeOffset clippedStart = trace.ArrivalUtc < windowStart ? windowStart : trace.ArrivalUtc;
            DateTimeOffset clippedEnd = trace.CompletionUtc > windowEnd ? windowEnd : trace.CompletionUtc;

            if (clippedEnd > clippedStart)
            {
                accumulatedLifetimeMs += (clippedEnd - clippedStart).TotalMilliseconds;
            }
        }

        return accumulatedLifetimeMs / windowDurationMs;
    }

    private static bool IsRejectedRequest(RequestTraceRecord trace) =>
        trace.RateLimited || trace.StatusCode == 429;

    private static bool IsTimeoutRequest(RequestTraceRecord trace) =>
        trace.StatusCode == 504 ||
        (!string.IsNullOrWhiteSpace(trace.ErrorCode) &&
         trace.ErrorCode.Contains("timeout", StringComparison.OrdinalIgnoreCase));
}
