using System.Text.Json;
using Lab.Shared.Configuration;
using Lab.Shared.IO;
using Lab.Shared.Logging;
using Lab.Telemetry.Persistence;
using Lab.Telemetry.RequestTracing;
using Microsoft.Extensions.Logging;

namespace Lab.UnitTests;

[TestClass]
public sealed class JsonlTelemetryWriterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task JsonlRequestTraceWriter_WritesSingleLineMachineParseableJson()
    {
        string root = CreateUniqueTempDirectory();
        string path = Path.Combine(root, "logs", "requests.jsonl");

        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => { });
        IRequestTraceWriter writer = new JsonlRequestTraceWriter(
            path,
            new SafeFileAppender(),
            loggerFactory.CreateLogger<JsonlRequestTraceWriter>());

        RequestTraceRecord record = CreateRequestTraceRecord();
        bool persisted = await writer.WriteAsync(record);

        Assert.IsTrue(persisted);

        string[] lines = File.ReadAllLines(path);
        Assert.HasCount(1, lines);

        RequestTraceRecord? parsed = JsonSerializer.Deserialize<RequestTraceRecord>(lines[0], JsonOptions);

        Assert.IsNotNull(parsed);
        Assert.AreEqual(record.TraceId, parsed.TraceId);
        Assert.AreEqual(record.StageTimings[0].StageName, parsed.StageTimings[0].StageName);
        Assert.AreEqual(record.DependencyCalls[0].Metadata["downstreamTraceId"], parsed.DependencyCalls[0].Metadata["downstreamTraceId"]);
        Assert.AreEqual(record.Notes[0], parsed.Notes[0]);
    }

    [TestMethod]
    public async Task JsonlJobTraceWriter_WritesSingleLineMachineParseableJson()
    {
        string root = CreateUniqueTempDirectory();
        string path = Path.Combine(root, "logs", "jobs.jsonl");

        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => { });
        IJobTraceWriter writer = new JsonlJobTraceWriter(
            path,
            new SafeFileAppender(),
            loggerFactory.CreateLogger<JsonlJobTraceWriter>());

        JobTraceRecord record = CreateJobTraceRecord();
        bool persisted = await writer.WriteAsync(record);

        Assert.IsTrue(persisted);

        string[] lines = File.ReadAllLines(path);
        Assert.HasCount(1, lines);

        JobTraceRecord? parsed = JsonSerializer.Deserialize<JobTraceRecord>(lines[0], JsonOptions);

        Assert.IsNotNull(parsed);
        Assert.AreEqual(record.JobId, parsed.JobId);
        Assert.AreEqual(record.StageTimings[0].StageName, parsed.StageTimings[0].StageName);
        Assert.AreEqual(record.DependencyCalls[0].DependencyName, parsed.DependencyCalls[0].DependencyName);
    }

    [TestMethod]
    public async Task JsonlRequestTraceWriter_ConcurrentWritesRemainLineDelimitedAndParseable()
    {
        string root = CreateUniqueTempDirectory();
        string path = Path.Combine(root, "logs", "requests.jsonl");

        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => { });
        SafeFileAppender appender = new();

        Task<bool>[] writes = Enumerable.Range(1, 200)
            .Select(index =>
            {
                IRequestTraceWriter writer = new JsonlRequestTraceWriter(
                    path,
                    appender,
                    loggerFactory.CreateLogger<JsonlRequestTraceWriter>());

                RequestTraceRecord record = CreateRequestTraceRecord() with
                {
                    TraceId = $"trace-{index:D4}",
                    RequestId = $"request-{index:D4}"
                };

                return writer.WriteAsync(record).AsTask();
            })
            .ToArray();

        bool[] persisted = await Task.WhenAll(writes);

        CollectionAssert.DoesNotContain(persisted, false);

        string[] lines = File.ReadAllLines(path);
        Assert.HasCount(200, lines);

        foreach (string line in lines)
        {
            RequestTraceRecord? parsed = JsonSerializer.Deserialize<RequestTraceRecord>(line, JsonOptions);
            Assert.IsNotNull(parsed);
            Assert.IsFalse(string.IsNullOrWhiteSpace(parsed.TraceId));
        }
    }

    [TestMethod]
    public void LabOperationalFileLoggerProvider_AppendsToTheServiceSpecificLogFile()
    {
        string root = CreateUniqueTempDirectory();
        string path = Path.Combine(root, "logs", "storefront.log");
        EnvironmentLayout layout = new(
            RepositoryRoot: root,
            SourceRoot: Path.Combine(root, "src"),
            ContentRoot: Path.Combine(root, "src", "Storefront.Api"),
            ServiceName: "Storefront.Api",
            CurrentRegion: "local",
            PrimaryDatabasePath: Path.Combine(root, "data", "primary.db"),
            ReplicaEastDatabasePath: Path.Combine(root, "data", "replica-east.db"),
            ReplicaWestDatabasePath: Path.Combine(root, "data", "replica-west.db"),
            ReadModelDatabasePath: Path.Combine(root, "data", "readmodels.db"),
            RequestsJsonlPath: Path.Combine(root, "logs", "requests.jsonl"),
            JobsJsonlPath: Path.Combine(root, "logs", "jobs.jsonl"),
            RunsDirectory: Path.Combine(root, "logs", "runs"),
            AnalysisDirectory: Path.Combine(root, "analysis"),
            ServiceLogPath: path);

        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            builder.AddProvider(new LabOperationalFileLoggerProvider(layout, new SafeFileAppender())));

        ILogger logger = loggerFactory.CreateLogger("Tests.Operational");
        logger.LogInformation("Hello {Target}", "world");

        string contents = File.ReadAllText(path);
        StringAssert.Contains(contents, "[Information]");
        StringAssert.Contains(contents, "Tests.Operational");
        StringAssert.Contains(contents, "Hello world");
    }

    private static RequestTraceRecord CreateRequestTraceRecord()
    {
        DateTimeOffset started = new(2026, 03, 31, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset ended = started.AddMilliseconds(12);

        return new RequestTraceRecord
        {
            RunId = "run-001",
            TraceId = "trace-001",
            SpanId = "span-001",
            RequestId = "request-001",
            Operation = "product-page",
            Region = "local",
            Service = "Storefront.Api",
            Route = "/products/sku-123",
            Method = "GET",
            ArrivalUtc = started,
            StartUtc = started,
            CompletionUtc = ended,
            LatencyMs = 12,
            StatusCode = 200,
            ContractSatisfied = true,
            CacheHit = false,
            RateLimited = false,
            DependencyCalls =
            [
                new DependencyCallRecord
                {
                    DependencyName = "catalog-api",
                    Route = "/catalog/products/sku-123",
                    Region = "local",
                    StartedUtc = started.AddMilliseconds(3),
                    EndedUtc = started.AddMilliseconds(8),
                    ElapsedMs = 5,
                    StatusCode = 200,
                    Outcome = "success",
                    Metadata = new Dictionary<string, string?>
                    {
                        ["downstreamTraceId"] = "trace-downstream-001"
                    },
                    Notes = ["line1\nline2"]
                }
            ],
            StageTimings =
            [
                new StageTimingRecord
                {
                    StageName = "compose_product_page",
                    StartedUtc = started,
                    EndedUtc = started.AddMilliseconds(2),
                    ElapsedMs = 2,
                    Outcome = "success",
                    Metadata = new Dictionary<string, string?>
                    {
                        ["cache"] = "miss"
                    }
                }
            ],
            Notes = ["top-level\nnote"]
        };
    }

    private static JobTraceRecord CreateJobTraceRecord()
    {
        DateTimeOffset enqueued = new(2026, 03, 31, 12, 0, 0, TimeSpan.Zero);

        return new JobTraceRecord
        {
            RunId = "run-002",
            TraceId = "trace-002",
            JobId = "job-001",
            JobType = "projection-heartbeat",
            Region = "local",
            Service = "Worker",
            Status = "completed",
            EnqueuedUtc = enqueued,
            DequeuedUtc = enqueued.AddMilliseconds(4),
            ExecutionStartUtc = enqueued.AddMilliseconds(4),
            ExecutionEndUtc = enqueued.AddMilliseconds(15),
            QueueDelayMs = 4,
            ExecutionMs = 11,
            RetryCount = 0,
            ContractSatisfied = true,
            DependencyCalls =
            [
                new DependencyCallRecord
                {
                    DependencyName = "read-model-self-check",
                    Route = "/internal/read-model/ping",
                    Region = "local",
                    StartedUtc = enqueued.AddMilliseconds(6),
                    EndedUtc = enqueued.AddMilliseconds(10),
                    ElapsedMs = 4,
                    StatusCode = 200,
                    Outcome = "success",
                    Metadata = new Dictionary<string, string?>
                    {
                        ["probe"] = "heartbeat"
                    },
                    Notes = ["heartbeat"]
                }
            ],
            StageTimings =
            [
                new StageTimingRecord
                {
                    StageName = "projection_update",
                    StartedUtc = enqueued.AddMilliseconds(4),
                    EndedUtc = enqueued.AddMilliseconds(12),
                    ElapsedMs = 8,
                    Outcome = "success",
                    Metadata = new Dictionary<string, string?>
                    {
                        ["jobType"] = "projection-heartbeat"
                    }
                }
            ],
            Notes = ["job-note"]
        };
    }

    private static string CreateUniqueTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
