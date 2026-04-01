using Lab.Analysis.Models;
using Lab.Analysis.Services;
using Lab.Persistence;
using Lab.Persistence.Queueing;
using Lab.Telemetry.RequestTracing;
using Microsoft.Extensions.Logging;

namespace Lab.UnitTests;

[TestClass]
public sealed class TelemetryAnalyzerTests
{
    [TestMethod]
    public void Analyze_ComputesLatencyPercentilesThroughputAndExactAverageConcurrency()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => { });
        TelemetryAnalyzer analyzer = new(
            new TelemetryJsonlReader(loggerFactory.CreateLogger<TelemetryJsonlReader>()),
            new QueueMetricsReader(),
            new FixedTimeProvider(new DateTimeOffset(2026, 03, 31, 13, 0, 0, TimeSpan.Zero)));

        AnalysisSummary summary = analyzer.Analyze(
            requestTraces:
            [
                CreateRequestTrace("run-a", "trace-1", arrivalMs: 0, completionMs: 4000, statusCode: 200, cacheHit: true),
                CreateRequestTrace("run-a", "trace-2", arrivalMs: 1000, completionMs: 5000, statusCode: 500, rateLimited: false),
                CreateRequestTrace("run-a", "trace-3", arrivalMs: 4000, completionMs: 6000, statusCode: 429, rateLimited: true)
            ],
            jobTraces:
            [
                CreateJobTrace("run-a", "job-1", queueDelayMs: 20, executionMs: 80, retryCount: 0),
                CreateJobTrace("run-a", "job-2", queueDelayMs: 40, executionMs: 60, retryCount: 1)
            ],
            filter: new AnalysisFilter("run-a", null, null, "product-page"),
            requestsPath: "logs/requests.jsonl",
            jobsPath: "logs/jobs.jsonl");

        Assert.AreEqual("run-a", summary.RunId);
        Assert.AreEqual("product-page", summary.FilterOperation);
        Assert.AreEqual(3, summary.Requests.RequestCount);
        Assert.AreEqual(3, summary.Requests.CompletedRequestCount);
        Assert.AreEqual(3333.3333333333335d, summary.Requests.AverageLatencyMs!.Value, 0.0001d);
        Assert.AreEqual(4000d, summary.Requests.P50LatencyMs!.Value, 0.0001d);
        Assert.AreEqual(4000d, summary.Requests.P95LatencyMs!.Value, 0.0001d);
        Assert.AreEqual(4000d, summary.Requests.P99LatencyMs!.Value, 0.0001d);
        Assert.AreEqual(0.5d, summary.Requests.ThroughputPerSecond!.Value, 0.0001d);
        Assert.AreEqual(1.6666666666666667d, summary.Requests.AverageConcurrency!.Value, 0.0001d);
        Assert.AreEqual(0.3333333333333333d, summary.Requests.CacheHitRate!.Value, 0.0001d);
        Assert.AreEqual(0.6666666666666666d, summary.Requests.CacheMissRate!.Value, 0.0001d);
        Assert.AreEqual(0.3333333333333333d, summary.Requests.RateLimitedFraction!.Value, 0.0001d);
        Assert.AreEqual(1, summary.Requests.ErrorCounts["429"]);
        Assert.AreEqual(1, summary.Requests.ErrorCounts["500"]);
        Assert.AreEqual(0, summary.ReadFreshness.ReadRequestCount);
        Assert.AreEqual(1, summary.Overload.RejectedRequestCount);
        Assert.AreEqual(0.3333333333333333d, summary.Overload.RejectFraction!.Value, 0.0001d);
        Assert.AreEqual(2, summary.Overload.AdmittedRequestCount);
        Assert.AreEqual(0.6666666666666666d, summary.Overload.AdmittedFraction!.Value, 0.0001d);
        Assert.AreEqual(1, summary.Overload.RetriedJobCount);
        Assert.AreEqual(1, summary.Overload.TotalRetryAttempts);
        Assert.AreEqual(2, summary.Overload.AdmittedRequests.RequestCount);
        Assert.AreEqual(0.3333333333333333d, summary.Overload.RateLimitedRequests.Fraction!.Value, 0.0001d);
        Assert.AreEqual(0.3333333333333333d, summary.Overload.AdmittedRequests.ThroughputPerSecond!.Value, 0.0001d);
        Assert.AreEqual(4000d, summary.Overload.AdmittedRequests.P95LatencyMs!.Value, 0.0001d);
        Assert.IsFalse(summary.Queue.Captured);
        Assert.AreEqual(2, summary.Jobs.JobCount);
        Assert.AreEqual(30d, summary.Jobs.AverageQueueDelayMs!.Value, 0.0001d);
        Assert.AreEqual(40d, summary.Jobs.P95QueueDelayMs!.Value, 0.0001d);
        Assert.AreEqual(70d, summary.Jobs.AverageExecutionMs!.Value, 0.0001d);
        Assert.AreEqual(80d, summary.Jobs.P95ExecutionMs!.Value, 0.0001d);
        Assert.AreEqual(1, summary.Jobs.RetryCountDistribution["0"]);
        Assert.AreEqual(1, summary.Jobs.RetryCountDistribution["1"]);
    }

    [TestMethod]
    public async Task AnalyzeAsync_ReadsJsonlAndWritesSummaryAndMarkdownArtifacts()
    {
        string root = CreateUniqueTempDirectory();
        string requestsPath = Path.Combine(root, "logs", "requests.jsonl");
        string jobsPath = Path.Combine(root, "logs", "jobs.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(requestsPath)!);

        await File.WriteAllLinesAsync(requestsPath,
        [
            Serialize(CreateRequestTrace("run-b", "trace-10", arrivalMs: 0, completionMs: 1000, statusCode: 200, cacheHit: true)),
            Serialize(CreateRequestTrace("run-b", "trace-11", arrivalMs: 1000, completionMs: 2500, statusCode: 200))
        ]);

        await File.WriteAllLinesAsync(jobsPath,
        [
            Serialize(CreateJobTrace("run-b", "job-10", queueDelayMs: 10, executionMs: 20, retryCount: 0))
        ]);

        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => { });
        TelemetryAnalyzer analyzer = new(
            new TelemetryJsonlReader(loggerFactory.CreateLogger<TelemetryJsonlReader>()),
            new QueueMetricsReader(),
            new FixedTimeProvider(new DateTimeOffset(2026, 03, 31, 14, 0, 0, TimeSpan.Zero)));
        AnalysisArtifactWriter artifactWriter = new();

        AnalysisSummary summary = await analyzer.AnalyzeAsync(
            requestsPath,
            jobsPath,
            null,
            new AnalysisFilter("run-b", null, null, "product-page"));

        string summaryPath = Path.Combine(root, "logs", "runs", summary.RunId, "summary.json");
        string reportPath = Path.Combine(root, "analysis", summary.RunId, "report.md");

        await artifactWriter.WriteSummaryJsonAsync(summary, summaryPath);
        await artifactWriter.WriteMarkdownReportAsync(summary, reportPath);

        string summaryContents = await File.ReadAllTextAsync(summaryPath);
        string reportContents = await File.ReadAllTextAsync(reportPath);

        StringAssert.Contains(summaryContents, "\"runId\": \"run-b\"");
        StringAssert.Contains(summaryContents, "\"filterOperation\": \"product-page\"");
        StringAssert.Contains(summaryContents, "\"averageConcurrency\"");
        StringAssert.Contains(summaryContents, "\"queue\"");
        StringAssert.Contains(summaryContents, "\"overload\"");
        StringAssert.Contains(summaryContents, "\"admittedRequests\"");
        StringAssert.Contains(summaryContents, "\"readFreshness\"");
        StringAssert.Contains(reportContents, "# Analysis Report: run-b");
        StringAssert.Contains(reportContents, "## Contract");
        StringAssert.Contains(reportContents, "## Observation Boundary");
        StringAssert.Contains(reportContents, "## Topology");
        StringAssert.Contains(reportContents, "## Workload");
        StringAssert.Contains(reportContents, "## Results");
        StringAssert.Contains(reportContents, "### Queue State Snapshot");
        StringAssert.Contains(reportContents, "### Request Metrics");
        StringAssert.Contains(reportContents, "### Read Freshness Metrics");
        StringAssert.Contains(reportContents, "### Overload Metrics");
        StringAssert.Contains(reportContents, "### Rate-Limited Requests");
        StringAssert.Contains(reportContents, "### Admitted Requests");
        StringAssert.Contains(reportContents, "### Processed Job Metrics");
        StringAssert.Contains(reportContents, "Filter operation");
        StringAssert.Contains(reportContents, "Detailed process counts, regions, and dependency layouts must come from the surrounding experiment docs");
        StringAssert.Contains(reportContents, "## Interpretation");
        StringAssert.Contains(reportContents, "## Architectural Justification");
        StringAssert.Contains(reportContents, "reconstructed exactly from observed lifetimes");
        StringAssert.Contains(reportContents, "No live queue snapshot was captured");
    }

    [TestMethod]
    public async Task AnalyzeAsync_IncludesFilteredLiveQueueSnapshotWhenPrimaryDbExists()
    {
        string root = CreateUniqueTempDirectory();
        string requestsPath = Path.Combine(root, "logs", "requests.jsonl");
        string jobsPath = Path.Combine(root, "logs", "jobs.jsonl");
        string primaryDbPath = Path.Combine(root, "data", "primary.db");
        Directory.CreateDirectory(Path.GetDirectoryName(requestsPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(primaryDbPath)!);
        await File.WriteAllTextAsync(requestsPath, string.Empty);
        await File.WriteAllTextAsync(jobsPath, string.Empty);

        PrimaryDbContextFactory dbContextFactory = new();
        PrimaryDatabaseInitializer initializer = new(dbContextFactory);
        await initializer.InitializeAsync(primaryDbPath);

        await using (PrimaryDbContext dbContext = dbContextFactory.CreateDbContext(primaryDbPath))
        {
            SqliteDurableQueueStore queueStore = new(dbContext);
            DateTimeOffset snapshotUtc = new(2026, 03, 31, 15, 0, 0, TimeSpan.Zero);

            await queueStore.EnqueueAsync(new EnqueueQueueJobRequest(
                QueueJobId: "job-054-analysis-001",
                JobType: "payment-confirmation-retry",
                PayloadJson: Serialize(new { paymentId = "pay-1", orderId = "order-1", runId = "run-c" }),
                EnqueuedUtc: snapshotUtc.AddSeconds(-10)));

            await queueStore.EnqueueAsync(new EnqueueQueueJobRequest(
                QueueJobId: "job-054-analysis-002",
                JobType: "payment-confirmation-retry",
                PayloadJson: Serialize(new { paymentId = "pay-2", orderId = "order-2", runId = "run-c" }),
                EnqueuedUtc: snapshotUtc.AddSeconds(-6),
                AvailableUtc: snapshotUtc.AddSeconds(20)));

            await queueStore.EnqueueAsync(new EnqueueQueueJobRequest(
                QueueJobId: "job-054-analysis-003",
                JobType: "payment-confirmation-retry",
                PayloadJson: Serialize(new { paymentId = "pay-3", orderId = "order-3", runId = "run-d" }),
                EnqueuedUtc: snapshotUtc.AddSeconds(-25)));
        }

        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => { });
        TelemetryAnalyzer analyzer = new(
            new TelemetryJsonlReader(loggerFactory.CreateLogger<TelemetryJsonlReader>()),
            new QueueMetricsReader(),
            new FixedTimeProvider(new DateTimeOffset(2026, 03, 31, 15, 0, 0, TimeSpan.Zero)));

        AnalysisSummary summary = await analyzer.AnalyzeAsync(
            requestsPath,
            jobsPath,
            primaryDbPath,
            new AnalysisFilter("run-c", null, null, null));

        Assert.IsTrue(summary.Queue.Captured);
        Assert.AreEqual("run-c", summary.Queue.FilterRunId);
        Assert.AreEqual(2, summary.Queue.PendingCount);
        Assert.AreEqual(1, summary.Queue.ReadyCount);
        Assert.AreEqual(1, summary.Queue.DelayedCount);
        Assert.AreEqual(10_000d, summary.Queue.OldestQueuedAgeMs!.Value, 0.001d);
    }

    [TestMethod]
    public void Analyze_ComputesOverloadBreakdownForMixedRejectedTimedOutAndAdmittedRequests()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => { });
        TelemetryAnalyzer analyzer = new(
            new TelemetryJsonlReader(loggerFactory.CreateLogger<TelemetryJsonlReader>()),
            new QueueMetricsReader(),
            new FixedTimeProvider(new DateTimeOffset(2026, 03, 31, 16, 0, 0, TimeSpan.Zero)));

        AnalysisSummary summary = analyzer.Analyze(
            requestTraces:
            [
                CreateRequestTrace("run-overload", "trace-1", arrivalMs: 0, completionMs: 1000, statusCode: 200),
                CreateRequestTrace("run-overload", "trace-2", arrivalMs: 1000, completionMs: 3000, statusCode: 200),
                CreateRequestTrace("run-overload", "trace-3", arrivalMs: 2000, completionMs: 2600, statusCode: 429, rateLimited: true, errorCode: "rate_limited"),
                CreateRequestTrace("run-overload", "trace-4", arrivalMs: 3000, completionMs: 6500, statusCode: 504, errorCode: "simulated_timeout")
            ],
            jobTraces:
            [
                CreateJobTrace("run-overload", "job-1", queueDelayMs: 10, executionMs: 40, retryCount: 2),
                CreateJobTrace("run-overload", "job-2", queueDelayMs: 20, executionMs: 40, retryCount: 0)
            ],
            filter: new AnalysisFilter("run-overload", null, null, "product-page"),
            requestsPath: "logs/requests.jsonl",
            jobsPath: "logs/jobs.jsonl");

        Assert.AreEqual(1, summary.Overload.RejectedRequestCount);
        Assert.AreEqual(0.25d, summary.Overload.RejectFraction!.Value, 0.0001d);
        Assert.AreEqual(1, summary.Overload.TimeoutRequestCount);
        Assert.AreEqual(0.25d, summary.Overload.TimeoutFraction!.Value, 0.0001d);
        Assert.AreEqual(3, summary.Overload.AdmittedRequestCount);
        Assert.AreEqual(0.75d, summary.Overload.AdmittedFraction!.Value, 0.0001d);
        Assert.AreEqual(1, summary.Overload.RetriedJobCount);
        Assert.AreEqual(2, summary.Overload.TotalRetryAttempts);
        Assert.AreEqual(1, summary.Overload.RateLimitedRequests.RequestCount);
        Assert.AreEqual(0.15384615384615385d, summary.Overload.RateLimitedRequests.ThroughputPerSecond!.Value, 0.0001d);
        Assert.AreEqual(3, summary.Overload.AdmittedRequests.RequestCount);
        Assert.AreEqual(0.46153846153846156d, summary.Overload.AdmittedRequests.ThroughputPerSecond!.Value, 0.0001d);
        Assert.AreEqual(3500d, summary.Overload.AdmittedRequests.P95LatencyMs!.Value, 0.0001d);
        Assert.AreEqual(1, summary.Overload.AdmittedRequests.ErrorCounts["504"]);
    }

    [TestMethod]
    public void Analyze_ComputesReadFreshnessMetricsGroupedBySource()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => { });
        TelemetryAnalyzer analyzer = new(
            new TelemetryJsonlReader(loggerFactory.CreateLogger<TelemetryJsonlReader>()),
            new QueueMetricsReader(),
            new FixedTimeProvider(new DateTimeOffset(2026, 03, 31, 17, 0, 0, TimeSpan.Zero)));

        AnalysisSummary summary = analyzer.Analyze(
            requestTraces:
            [
                CreateRequestTrace("run-freshness", "trace-1", arrivalMs: 0, completionMs: 100, statusCode: 200, readSource: "primary", freshnessComparedCount: 1, freshnessStaleCount: 0),
                CreateRequestTrace("run-freshness", "trace-2", arrivalMs: 100, completionMs: 220, statusCode: 200, readSource: "replica-east", freshnessComparedCount: 1, freshnessStaleCount: 1, maxStalenessAgeMs: 250d),
                CreateRequestTrace("run-freshness", "trace-3", arrivalMs: 220, completionMs: 300, statusCode: 200, readSource: "replica-east", freshnessComparedCount: 1, freshnessStaleCount: 0),
                CreateRequestTrace("run-freshness", "trace-4", arrivalMs: 300, completionMs: 360, statusCode: 200, readSource: "read-model", freshnessComparedCount: 2, freshnessStaleCount: 1, maxStalenessAgeMs: 40d)
            ],
            jobTraces: Array.Empty<JobTraceRecord>(),
            filter: new AnalysisFilter("run-freshness", null, null, "product-page"),
            requestsPath: "logs/requests.jsonl",
            jobsPath: "logs/jobs.jsonl");

        Assert.AreEqual(4, summary.ReadFreshness.ReadRequestCount);
        Assert.AreEqual(2, summary.ReadFreshness.StaleRequestCount);
        Assert.AreEqual(0.5d, summary.ReadFreshness.StaleRequestFraction!.Value, 0.0001d);
        Assert.AreEqual(5, summary.ReadFreshness.ComparedResultCount);
        Assert.AreEqual(2, summary.ReadFreshness.StaleResultCount);
        Assert.AreEqual(0.4d, summary.ReadFreshness.StaleResultFraction!.Value, 0.0001d);
        Assert.AreEqual(145d, summary.ReadFreshness.AverageMaxStalenessAgeMs!.Value, 0.0001d);
        Assert.AreEqual(250d, summary.ReadFreshness.MaxObservedStalenessAgeMs!.Value, 0.0001d);

        Assert.HasCount(3, summary.ReadFreshness.Sources);

        ReadSourceFreshnessMetricsSummary primary = summary.ReadFreshness.Sources.Single(source => source.ReadSource == "primary");
        Assert.AreEqual(1, primary.RequestCount);
        Assert.AreEqual(0, primary.StaleRequestCount);
        Assert.AreEqual(100d, primary.AverageLatencyMs!.Value, 0.0001d);
        Assert.AreEqual(100d, primary.P95LatencyMs!.Value, 0.0001d);

        ReadSourceFreshnessMetricsSummary replicaEast = summary.ReadFreshness.Sources.Single(source => source.ReadSource == "replica-east");
        Assert.AreEqual(2, replicaEast.RequestCount);
        Assert.AreEqual(1, replicaEast.StaleRequestCount);
        Assert.AreEqual(0.5d, replicaEast.StaleRequestFraction!.Value, 0.0001d);
        Assert.AreEqual(100d, replicaEast.AverageLatencyMs!.Value, 0.0001d);
        Assert.AreEqual(120d, replicaEast.P95LatencyMs!.Value, 0.0001d);
        Assert.AreEqual(250d, replicaEast.AverageMaxStalenessAgeMs!.Value, 0.0001d);
        Assert.AreEqual(250d, replicaEast.MaxObservedStalenessAgeMs!.Value, 0.0001d);
    }

    private static RequestTraceRecord CreateRequestTrace(
        string runId,
        string traceId,
        int arrivalMs,
        int completionMs,
        int statusCode,
        bool cacheHit = false,
        bool rateLimited = false,
        string? errorCode = null,
        string? readSource = null,
        int? freshnessComparedCount = null,
        int? freshnessStaleCount = null,
        double? maxStalenessAgeMs = null)
    {
        DateTimeOffset origin = new(2026, 03, 31, 12, 0, 0, TimeSpan.Zero);

        return new RequestTraceRecord
        {
            RunId = runId,
            TraceId = traceId,
            SpanId = $"span-{traceId}",
            RequestId = $"request-{traceId}",
            Operation = "product-page",
            Region = "local",
            Service = "Storefront.Api",
            Route = "/products/demo",
            Method = "GET",
            ArrivalUtc = origin.AddMilliseconds(arrivalMs),
            StartUtc = origin.AddMilliseconds(arrivalMs),
            CompletionUtc = origin.AddMilliseconds(completionMs),
            LatencyMs = completionMs - arrivalMs,
            StatusCode = statusCode,
            ContractSatisfied = statusCode < 500,
            CacheHit = cacheHit,
            RateLimited = rateLimited,
            DependencyCalls = Array.Empty<DependencyCallRecord>(),
            StageTimings = Array.Empty<StageTimingRecord>(),
            ErrorCode = errorCode,
            ReadSource = readSource,
            FreshnessComparedCount = freshnessComparedCount,
            FreshnessStaleCount = freshnessStaleCount,
            FreshnessStaleFraction = freshnessComparedCount.HasValue && freshnessStaleCount.HasValue && freshnessComparedCount.Value > 0
                ? freshnessStaleCount.Value / (double)freshnessComparedCount.Value
                : null,
            MaxStalenessAgeMs = maxStalenessAgeMs,
            Notes = Array.Empty<string>()
        };
    }

    private static JobTraceRecord CreateJobTrace(
        string runId,
        string jobId,
        int queueDelayMs,
        int executionMs,
        int retryCount)
    {
        DateTimeOffset origin = new(2026, 03, 31, 12, 0, 0, TimeSpan.Zero);

        return new JobTraceRecord
        {
            RunId = runId,
            TraceId = $"trace-{jobId}",
            JobId = jobId,
            JobType = "projection-heartbeat",
            Region = "local",
            Service = "Worker",
            Status = "completed",
            EnqueuedUtc = origin,
            DequeuedUtc = origin.AddMilliseconds(queueDelayMs),
            ExecutionStartUtc = origin.AddMilliseconds(queueDelayMs),
            ExecutionEndUtc = origin.AddMilliseconds(queueDelayMs + executionMs),
            QueueDelayMs = queueDelayMs,
            ExecutionMs = executionMs,
            RetryCount = retryCount,
            ContractSatisfied = true,
            DependencyCalls = Array.Empty<DependencyCallRecord>(),
            StageTimings = Array.Empty<StageTimingRecord>(),
            Notes = Array.Empty<string>()
        };
    }

    private static string CreateUniqueTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Serialize<T>(T value) =>
        System.Text.Json.JsonSerializer.Serialize(value, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
