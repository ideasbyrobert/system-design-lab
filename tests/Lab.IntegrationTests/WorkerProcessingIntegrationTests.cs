using System.Text.Json;
using System.Net.Http.Json;
using Lab.Persistence;
using Lab.Persistence.DependencyInjection;
using Lab.Persistence.Entities;
using Lab.Persistence.Queueing;
using Lab.Persistence.Seeding;
using Lab.Shared.Configuration;
using Lab.Shared.Http;
using Lab.Shared.Queueing;
using Lab.Telemetry.RequestTracing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Worker;
using Worker.DependencyInjection;
using Worker.Jobs;

namespace Lab.IntegrationTests;

[TestClass]
public sealed class WorkerProcessingIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task PaymentConfirmationRetry_TransientFailure_ReschedulesThenCompletes()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        await using PaymentSimulatorFactory paymentFactory = new(repositoryRoot, CreateFastPaymentOverrides());
        using HttpClient paymentClient = paymentFactory.CreateClient();
        using IHost workerHost = CreateWorkerHost(repositoryRoot, paymentClient);

        EnvironmentLayout layout = workerHost.Services.GetRequiredService<EnvironmentLayout>();
        await SeedPendingPaymentAsync(layout.PrimaryDatabasePath, paymentMode: "transient_failure", paymentId: "pay-051-transient-001", orderId: "order-051-transient-001");

        await EnqueueJobAsync(
            workerHost,
            "job-051-transient-001",
            new PaymentConfirmationRetryJobPayload("pay-051-transient-001", "order-051-transient-001", "worker-run-051-transient"));

        WorkerQueueProcessor processor = workerHost.Services.GetRequiredService<WorkerQueueProcessor>();

        Assert.AreEqual(1, await processor.ProcessAvailableJobsAsync(CancellationToken.None));

        QueueJobRecord firstState = await GetRequiredJobAsync(workerHost, "job-051-transient-001");
        Assert.AreEqual(QueueJobStatuses.Pending, firstState.Status);
        Assert.AreEqual(1, firstState.RetryCount);
        Assert.AreEqual("simulated_transient_failure", firstState.LastError);

        await Task.Delay(TimeSpan.FromMilliseconds(325));

        Assert.AreEqual(1, await processor.ProcessAvailableJobsAsync(CancellationToken.None));

        QueueJobRecord finalState = await GetRequiredJobAsync(workerHost, "job-051-transient-001");
        Assert.AreEqual(QueueJobStatuses.Completed, finalState.Status);
        Assert.AreEqual(1, finalState.RetryCount);

        await using (PrimaryDbContext dbContext = workerHost.Services.GetRequiredService<PrimaryDbContextFactory>().CreateDbContext(layout.PrimaryDatabasePath))
        {
            Payment payment = await dbContext.Payments.Include(item => item.Order).SingleAsync(item => item.PaymentId == "pay-051-transient-001");
            Assert.AreEqual("Authorized", payment.Status);
            Assert.AreEqual("Paid", payment.Order.Status);
        }

        QueueJobRecord historyProjectionJob = await GetRequiredJobByTypeAsync(workerHost, LabQueueJobTypes.OrderHistoryProjectionUpdate);
        Assert.AreEqual(QueueJobStatuses.Pending, historyProjectionJob.Status);

        IReadOnlyList<JobTraceTestHelper.TraceEnvelope> traces = await JobTraceTestHelper.ReadJobTracesAsync(repositoryRoot);
        JobTraceRecord[] jobTraces = traces
            .Where(item => item.Record.JobId == "job-051-transient-001")
            .Select(item => item.Record)
            .OrderBy(item => item.DequeuedUtc)
            .ToArray();

        Assert.HasCount(2, jobTraces);
        Assert.AreEqual(QueueJobStatuses.Pending, jobTraces[0].Status);
        Assert.AreEqual("simulated_transient_failure", jobTraces[0].ErrorCode);
        Assert.AreEqual(QueueJobStatuses.Completed, jobTraces[1].Status);
        Assert.IsTrue(jobTraces[1].ContractSatisfied);
        Assert.IsTrue(jobTraces[1].StageTimings.Any(stage => stage.StageName == "payment_state_persisted" && stage.Outcome == "paid"));
    }

    [TestMethod]
    public async Task PaymentConfirmationRetry_DelayedConfirmation_RewritesPayloadForStatusChecks()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        await using PaymentSimulatorFactory paymentFactory = new(repositoryRoot, CreateFastPaymentOverrides());
        using HttpClient paymentClient = paymentFactory.CreateClient();
        using IHost workerHost = CreateWorkerHost(repositoryRoot, paymentClient);

        EnvironmentLayout layout = workerHost.Services.GetRequiredService<EnvironmentLayout>();
        await SeedPendingPaymentAsync(layout.PrimaryDatabasePath, paymentMode: "delayed_confirmation", paymentId: "pay-051-delayed-001", orderId: "order-051-delayed-001");

        await EnqueueJobAsync(
            workerHost,
            "job-051-delayed-001",
            new PaymentConfirmationRetryJobPayload("pay-051-delayed-001", "order-051-delayed-001", "worker-run-051-delayed"));

        WorkerQueueProcessor processor = workerHost.Services.GetRequiredService<WorkerQueueProcessor>();

        Assert.AreEqual(1, await processor.ProcessAvailableJobsAsync(CancellationToken.None));

        QueueJobRecord firstState = await GetRequiredJobAsync(workerHost, "job-051-delayed-001");
        PaymentConfirmationRetryJobPayload firstPayload = JsonSerializer.Deserialize<PaymentConfirmationRetryJobPayload>(firstState.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Queue payload could not be deserialized.");

        Assert.AreEqual(QueueJobStatuses.Pending, firstState.Status);
        Assert.AreEqual(1, firstState.RetryCount);
        Assert.IsTrue(firstPayload.StatusCheckOnly);

        await Task.Delay(TimeSpan.FromMilliseconds(325));

        Assert.AreEqual(1, await processor.ProcessAvailableJobsAsync(CancellationToken.None));

        QueueJobRecord secondState = await GetRequiredJobAsync(workerHost, "job-051-delayed-001");
        PaymentConfirmationRetryJobPayload secondPayload = JsonSerializer.Deserialize<PaymentConfirmationRetryJobPayload>(secondState.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Queue payload could not be deserialized.");

        Assert.AreEqual(QueueJobStatuses.Pending, secondState.Status);
        Assert.AreEqual(2, secondState.RetryCount);
        Assert.IsTrue(secondPayload.StatusCheckOnly);

        IReadOnlyList<JobTraceTestHelper.TraceEnvelope> traces = await JobTraceTestHelper.ReadJobTracesAsync(repositoryRoot);
        JobTraceRecord[] jobTraces = traces
            .Where(item => item.Record.JobId == "job-051-delayed-001")
            .Select(item => item.Record)
            .OrderBy(item => item.DequeuedUtc)
            .ToArray();

        Assert.HasCount(2, jobTraces);
        Assert.AreEqual("/payments/authorize", jobTraces[0].DependencyCalls.Single().Route);
        Assert.AreEqual("/payments/authorizations/pay-051-delayed-001", jobTraces[1].DependencyCalls.Single().Route);
        Assert.AreEqual(QueueJobStatuses.Pending, jobTraces[0].Status);
        Assert.AreEqual(QueueJobStatuses.Pending, jobTraces[1].Status);
    }

    [TestMethod]
    public async Task PaymentConfirmationRetry_Timeout_FailsQueueJobAndMarksOrderFailed()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        await using PaymentSimulatorFactory paymentFactory = new(repositoryRoot, CreateFastPaymentOverrides());
        using HttpClient paymentClient = paymentFactory.CreateClient();
        using IHost workerHost = CreateWorkerHost(repositoryRoot, paymentClient);

        EnvironmentLayout layout = workerHost.Services.GetRequiredService<EnvironmentLayout>();
        await SeedPendingPaymentAsync(layout.PrimaryDatabasePath, paymentMode: "timeout", paymentId: "pay-051-timeout-001", orderId: "order-051-timeout-001");

        await EnqueueJobAsync(
            workerHost,
            "job-051-timeout-001",
            new PaymentConfirmationRetryJobPayload("pay-051-timeout-001", "order-051-timeout-001", "worker-run-051-timeout"));

        WorkerQueueProcessor processor = workerHost.Services.GetRequiredService<WorkerQueueProcessor>();
        Assert.AreEqual(1, await processor.ProcessAvailableJobsAsync(CancellationToken.None));

        QueueJobRecord queueJob = await GetRequiredJobAsync(workerHost, "job-051-timeout-001");
        Assert.AreEqual(QueueJobStatuses.Failed, queueJob.Status);
        Assert.AreEqual("simulated_timeout", queueJob.LastError);

        await using (PrimaryDbContext dbContext = workerHost.Services.GetRequiredService<PrimaryDbContextFactory>().CreateDbContext(layout.PrimaryDatabasePath))
        {
            Payment payment = await dbContext.Payments.Include(item => item.Order).SingleAsync(item => item.PaymentId == "pay-051-timeout-001");
            Assert.AreEqual("Timeout", payment.Status);
            Assert.AreEqual("simulated_timeout", payment.ErrorCode);
            Assert.AreEqual("Failed", payment.Order.Status);
        }

        QueueJobRecord historyProjectionJob = await GetRequiredJobByTypeAsync(workerHost, LabQueueJobTypes.OrderHistoryProjectionUpdate);
        Assert.AreEqual(QueueJobStatuses.Pending, historyProjectionJob.Status);

        JobTraceRecord trace = (await JobTraceTestHelper.ReadJobTracesAsync(repositoryRoot))
            .Single(item => item.Record.JobId == "job-051-timeout-001")
            .Record;

        Assert.AreEqual(QueueJobStatuses.Failed, trace.Status);
        Assert.AreEqual("simulated_timeout", trace.ErrorCode);
        Assert.IsFalse(trace.ContractSatisfied);
        Assert.IsTrue(trace.StageTimings.Any(stage => stage.StageName == "payment_state_persisted" && stage.Outcome == "failed"));
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

    private static IReadOnlyDictionary<string, string?> CreateFastPaymentOverrides() =>
        new Dictionary<string, string?>
        {
            ["Lab:PaymentSimulator:FastLatencyMilliseconds"] = "5",
            ["Lab:PaymentSimulator:SlowLatencyMilliseconds"] = "25",
            ["Lab:PaymentSimulator:TimeoutLatencyMilliseconds"] = "30",
            ["Lab:PaymentSimulator:DelayedConfirmationMilliseconds"] = "25",
            ["Lab:PaymentSimulator:DuplicateCallbackSpacingMilliseconds"] = "10",
            ["Lab:PaymentSimulator:DispatcherPollMilliseconds"] = "10"
        };

    private static async Task SeedPendingPaymentAsync(
        string primaryDatabasePath,
        string paymentMode,
        string paymentId,
        string orderId)
    {
        PrimaryDbContextFactory dbContextFactory = new();
        PrimaryDatabaseInitializer initializer = new(dbContextFactory);
        SqliteSeedDataService seeder = new(initializer, dbContextFactory);

        await seeder.SeedAsync(
            primaryDatabasePath,
            new SeedCounts(ProductCount: 1, UserCount: 1),
            resetExisting: true);

        await using PrimaryDbContext dbContext = dbContextFactory.CreateDbContext(primaryDatabasePath);
        dbContext.Orders.Add(new Lab.Persistence.Entities.Order
        {
            OrderId = orderId,
            UserId = "user-0001",
            Region = "local",
            Status = "PendingPayment",
            TotalPriceCents = 1999,
            CreatedUtc = DateTimeOffset.UtcNow
        });
        dbContext.Payments.Add(new Payment
        {
            PaymentId = paymentId,
            OrderId = orderId,
            Provider = "PaymentSimulator",
            IdempotencyKey = $"ikey-{paymentId}",
            Mode = paymentMode,
            Status = "Pending",
            AmountCents = 1999,
            AttemptedUtc = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync();
    }

    private static async Task EnqueueJobAsync(IHost workerHost, string jobId, PaymentConfirmationRetryJobPayload payload)
    {
        await using AsyncServiceScope scope = workerHost.Services.CreateAsyncScope();
        IDurableQueueStore queueStore = scope.ServiceProvider.GetRequiredService<IDurableQueueStore>();

        await queueStore.EnqueueAsync(
            new EnqueueQueueJobRequest(
                QueueJobId: jobId,
                JobType: LabQueueJobTypes.PaymentConfirmationRetry,
                PayloadJson: JsonSerializer.Serialize(payload, JsonOptions),
                EnqueuedUtc: DateTimeOffset.UtcNow.AddSeconds(-1)));
    }

    private static async Task<QueueJobRecord> GetRequiredJobAsync(IHost workerHost, string jobId)
    {
        await using AsyncServiceScope scope = workerHost.Services.CreateAsyncScope();
        IDurableQueueStore queueStore = scope.ServiceProvider.GetRequiredService<IDurableQueueStore>();
        QueueJobRecord? queueJob = await queueStore.GetByIdAsync(jobId);
        return queueJob ?? throw new InvalidOperationException($"Queue job '{jobId}' was not found.");
    }

    private static async Task<QueueJobRecord> GetRequiredJobByTypeAsync(IHost workerHost, string jobType)
    {
        await using AsyncServiceScope scope = workerHost.Services.CreateAsyncScope();
        EnvironmentLayout layout = scope.ServiceProvider.GetRequiredService<EnvironmentLayout>();
        PrimaryDbContextFactory dbContextFactory = scope.ServiceProvider.GetRequiredService<PrimaryDbContextFactory>();
        await using PrimaryDbContext dbContext = dbContextFactory.CreateDbContext(layout.PrimaryDatabasePath);

        QueueJob? queueJob = await dbContext.QueueJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.JobType == jobType);

        return queueJob is null
            ? throw new InvalidOperationException($"Queue job type '{jobType}' was not found.")
            : new QueueJobRecord(
                queueJob.QueueJobId,
                queueJob.JobType,
                queueJob.PayloadJson,
                queueJob.Status,
                queueJob.AvailableUtc,
                queueJob.EnqueuedUtc,
                queueJob.LeaseOwner,
                queueJob.LeaseExpiresUtc,
                queueJob.StartedUtc,
                queueJob.CompletedUtc,
                queueJob.RetryCount,
                queueJob.LastError);
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

            request.Headers.TryAddWithoutValidation(LabHeaderNames.RunId, runId);
            request.Headers.TryAddWithoutValidation(LabHeaderNames.CorrelationId, correlationId);

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
            request.Headers.TryAddWithoutValidation(LabHeaderNames.RunId, runId);
            request.Headers.TryAddWithoutValidation(LabHeaderNames.CorrelationId, correlationId);

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
