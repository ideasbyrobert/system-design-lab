using System.Text.Json;
using System.Net.Http.Json;
using Lab.Analysis.Models;
using Lab.Analysis.Services;
using Lab.Persistence;
using Lab.Persistence.DependencyInjection;
using Lab.Persistence.Entities;
using Lab.Persistence.Queueing;
using Lab.Persistence.Seeding;
using Lab.Shared.Configuration;
using Lab.Shared.Queueing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Worker;
using Worker.DependencyInjection;
using Worker.Jobs;

namespace Lab.IntegrationTests;

[TestClass]
public sealed class QueueBacklogAnalysisTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task AnalyzeAsync_BacklogReport_ShowsPendingQueueOldestAgeAndWaitingDominance()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        await using PaymentSimulatorFactory paymentFactory = new(repositoryRoot, CreateSlowPaymentOverrides());
        using HttpClient paymentClient = paymentFactory.CreateClient();
        using IHost workerHost = CreateWorkerHost(repositoryRoot, paymentClient);

        EnvironmentLayout layout = workerHost.Services.GetRequiredService<EnvironmentLayout>();
        await SeedPendingPaymentsAsync(layout.PrimaryDatabasePath, count: 3, paymentMode: "slow_success");
        await EnqueueBacklogJobsAsync(workerHost, "queue-backlog-054", count: 3);

        WorkerQueueProcessor processor = workerHost.Services.GetRequiredService<WorkerQueueProcessor>();
        Assert.AreEqual(1, await processor.ProcessAvailableJobsAsync(CancellationToken.None));

        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => { });
        TelemetryAnalyzer analyzer = new(
            new TelemetryJsonlReader(loggerFactory.CreateLogger<TelemetryJsonlReader>()),
            new QueueMetricsReader(),
            TimeProvider.System);
        AnalysisSummary summary = await analyzer.AnalyzeAsync(
            layout.RequestsJsonlPath,
            layout.JobsJsonlPath,
            layout.PrimaryDatabasePath,
            new AnalysisFilter("queue-backlog-054", null, null, null));

        Assert.IsTrue(summary.Queue.Captured);
        Assert.AreEqual("queue-backlog-054", summary.Queue.FilterRunId);
        Assert.AreEqual(3, summary.Queue.PendingCount);
        Assert.AreEqual(1, summary.Queue.CompletedCount);
        Assert.IsGreaterThan(0d, summary.Queue.OldestQueuedAgeMs!.Value);
        Assert.AreEqual(1, summary.Jobs.JobCount);
        Assert.IsNotNull(summary.Jobs.AverageQueueDelayMs);
        Assert.IsNotNull(summary.Jobs.AverageExecutionMs);
        Assert.IsGreaterThan(summary.Jobs.AverageExecutionMs!.Value, summary.Jobs.AverageQueueDelayMs!.Value);

        AnalysisArtifactWriter artifactWriter = new();
        string report = artifactWriter.BuildMarkdownReport(summary);

        StringAssert.Contains(report, "## Queue State Snapshot");
        StringAssert.Contains(report, "Oldest queued item age");
        StringAssert.Contains(report, "waiting dominates work");
    }

    private static IHost CreateWorkerHost(string repositoryRoot, HttpClient paymentClient)
    {
        HostApplicationBuilderSettings settings = new()
        {
            ApplicationName = "Worker",
            ContentRootPath = AppContext.BaseDirectory
        };

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(settings);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lab:Repository:RootPath"] = repositoryRoot,
            ["Lab:ServiceEndpoints:PaymentSimulatorBaseUrl"] = "http://127.0.0.1:65530",
            ["Lab:Queue:MaxDequeueBatchSize"] = "1"
        });
        builder.Services.AddLabConfiguration(builder.Configuration, builder.Environment);
        builder.Services.AddPrimaryPersistence();
        builder.Services.AddReadModelPersistence();
        builder.Services.AddLabWorkerProcessing();
        builder.Services.AddSingleton<IPaymentConfirmationClient>(new TestServerPaymentConfirmationClient(paymentClient));

        return builder.Build();
    }

    private static IReadOnlyDictionary<string, string?> CreateSlowPaymentOverrides() =>
        new Dictionary<string, string?>
        {
            ["Lab:PaymentSimulator:FastLatencyMilliseconds"] = "5",
            ["Lab:PaymentSimulator:SlowLatencyMilliseconds"] = "175",
            ["Lab:PaymentSimulator:TimeoutLatencyMilliseconds"] = "225",
            ["Lab:PaymentSimulator:DelayedConfirmationMilliseconds"] = "150",
            ["Lab:PaymentSimulator:DuplicateCallbackSpacingMilliseconds"] = "20",
            ["Lab:PaymentSimulator:DispatcherPollMilliseconds"] = "10"
        };

    private static async Task SeedPendingPaymentsAsync(string primaryDatabasePath, int count, string paymentMode)
    {
        PrimaryDbContextFactory dbContextFactory = new();
        PrimaryDatabaseInitializer initializer = new(dbContextFactory);
        SqliteSeedDataService seeder = new(initializer, dbContextFactory);

        await seeder.SeedAsync(
            primaryDatabasePath,
            new SeedCounts(ProductCount: 1, UserCount: 3),
            resetExisting: true);

        await using PrimaryDbContext dbContext = dbContextFactory.CreateDbContext(primaryDatabasePath);
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

        for (int index = 1; index <= count; index++)
        {
            string orderId = $"order-054-backlog-{index:D3}";
            string paymentId = $"pay-054-backlog-{index:D3}";

            dbContext.Orders.Add(new Lab.Persistence.Entities.Order
            {
                OrderId = orderId,
                UserId = $"user-000{index}",
                Region = "local",
                Status = "PendingPayment",
                TotalPriceCents = 1999 + index,
                CreatedUtc = nowUtc.AddMilliseconds(-index)
            });

            dbContext.Payments.Add(new Payment
            {
                PaymentId = paymentId,
                OrderId = orderId,
                Provider = "PaymentSimulator",
                IdempotencyKey = $"ikey-{paymentId}",
                Mode = paymentMode,
                Status = "Pending",
                AmountCents = 1999 + index,
                AttemptedUtc = nowUtc.AddMilliseconds(-index)
            });
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task EnqueueBacklogJobsAsync(IHost workerHost, string runId, int count)
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

        await using AsyncServiceScope scope = workerHost.Services.CreateAsyncScope();
        IDurableQueueStore queueStore = scope.ServiceProvider.GetRequiredService<IDurableQueueStore>();

        for (int index = 1; index <= count; index++)
        {
            string paymentId = $"pay-054-backlog-{index:D3}";
            string orderId = $"order-054-backlog-{index:D3}";

            await queueStore.EnqueueAsync(
                new EnqueueQueueJobRequest(
                    QueueJobId: $"job-054-backlog-{index:D3}",
                    JobType: LabQueueJobTypes.PaymentConfirmationRetry,
                    PayloadJson: JsonSerializer.Serialize(
                        new PaymentConfirmationRetryJobPayload(paymentId, orderId, runId),
                        JsonOptions),
                    EnqueuedUtc: nowUtc.AddMilliseconds(-1500 + (index * 100))));
        }
    }

    private static string CreateUniqueTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestServerPaymentConfirmationClient(HttpClient httpClient) : IPaymentConfirmationClient
    {
        public async Task<PaymentProviderObservation> AuthorizeAsync(
            PaymentAuthorizationCommand command,
            string runId,
            string correlationId,
            CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, $"/payments/authorize?mode={Uri.EscapeDataString(command.PaymentMode)}")
            {
                Content = JsonContent.Create(new
                {
                    command.PaymentId,
                    command.OrderId,
                    command.AmountCents,
                    command.Currency
                })
            };

            request.Headers.TryAddWithoutValidation(Lab.Shared.Http.LabHeaderNames.RunId, runId);
            request.Headers.TryAddWithoutValidation(Lab.Shared.Http.LabHeaderNames.CorrelationId, correlationId);

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            string payload = await response.Content.ReadAsStringAsync(cancellationToken);
            using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);

            return CreateAuthorizeObservation((int)response.StatusCode, document.RootElement);
        }

        public async Task<PaymentProviderObservation> GetStatusAsync(
            string paymentId,
            string runId,
            string correlationId,
            CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, $"/payments/authorizations/{Uri.EscapeDataString(paymentId)}");
            request.Headers.TryAddWithoutValidation(Lab.Shared.Http.LabHeaderNames.RunId, runId);
            request.Headers.TryAddWithoutValidation(Lab.Shared.Http.LabHeaderNames.CorrelationId, correlationId);

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            string payload = await response.Content.ReadAsStringAsync(cancellationToken);
            using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
            JsonElement root = document.RootElement;

            JsonElement callbacks = root.TryGetProperty("callbacks", out JsonElement callbackArray)
                ? callbackArray
                : default;

            int callbackCount = callbacks.ValueKind == JsonValueKind.Array ? callbacks.GetArrayLength() : 0;
            bool callbackPending = callbacks.ValueKind == JsonValueKind.Array &&
                                   callbacks.EnumerateArray().Any(item =>
                                       string.Equals(ReadString(item, "status"), "Pending", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(ReadString(item, "status"), "Dispatching", StringComparison.OrdinalIgnoreCase));

            return new PaymentProviderObservation(
                StatusCode: (int)response.StatusCode,
                Outcome: ReadString(root, "outcome") ?? (response.IsSuccessStatusCode ? "unknown" : "failed"),
                ErrorCode: ReadString(root, "error"),
                ErrorDetail: ReadString(root, "detail"),
                ProviderReference: ReadString(root, "latestProviderReference"),
                CallbackPending: callbackPending,
                CallbackCountScheduled: callbackCount,
                DownstreamRunId: ReadNestedString(root, "request", "runId"),
                DownstreamTraceId: ReadNestedString(root, "request", "traceId"),
                DownstreamRequestId: ReadNestedString(root, "request", "requestId"));
        }

        private static PaymentProviderObservation CreateAuthorizeObservation(int statusCode, JsonElement root) =>
            new(
                StatusCode: statusCode,
                Outcome: ReadString(root, "outcome") ?? (statusCode < 400 ? "unknown" : "failed"),
                ErrorCode: ReadString(root, "error"),
                ErrorDetail: ReadString(root, "detail"),
                ProviderReference: ReadString(root, "providerReference"),
                CallbackPending: root.TryGetProperty("callbackPending", out JsonElement callbackPending) && callbackPending.ValueKind == JsonValueKind.True,
                CallbackCountScheduled: ReadInt(root, "callbackCountScheduled") ?? 0,
                DownstreamRunId: ReadNestedString(root, "request", "runId"),
                DownstreamTraceId: ReadNestedString(root, "request", "traceId"),
                DownstreamRequestId: ReadNestedString(root, "request", "requestId"));

        private static string? ReadString(JsonElement root, string propertyName) =>
            root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static int? ReadInt(JsonElement root, string propertyName) =>
            root.TryGetProperty(propertyName, out JsonElement value) && value.TryGetInt32(out int parsed)
                ? parsed
                : null;

        private static string? ReadNestedString(JsonElement root, string parentPropertyName, string propertyName)
        {
            if (!root.TryGetProperty(parentPropertyName, out JsonElement parent))
            {
                return null;
            }

            return ReadString(parent, propertyName);
        }
    }
}
