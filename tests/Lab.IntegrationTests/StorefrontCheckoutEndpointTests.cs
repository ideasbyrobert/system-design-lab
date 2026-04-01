using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
using Order.Api.Checkout;
using Storefront.Api.Checkout;
using Worker;
using Worker.DependencyInjection;
using Worker.Jobs;
using CartEntity = Lab.Persistence.Entities.Cart;
using CartItemEntity = Lab.Persistence.Entities.CartItem;

namespace Lab.IntegrationTests;

[TestClass]
public sealed class StorefrontCheckoutEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task CheckoutEndpoint_AsyncMode_ReturnsPendingAtStorefrontBoundary_AndWorkerCanCompleteLater()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);
        await SeedActiveCartAsync(repositoryRoot, "user-0001", "sku-0001", 1);

        await using PaymentSimulatorFactory paymentFactory = new(repositoryRoot, CreateFastPaymentOverrides());
        using HttpClient paymentHttpClient = paymentFactory.CreateClient();
        await using OrderFactory orderFactory = new(
            repositoryRoot,
            configureServices: services =>
            {
                services.AddSingleton<IOrderPaymentClient>(new TestOrderPaymentClient(paymentHttpClient));
            });
        using HttpClient orderHttpClient = orderFactory.CreateClient();
        await using StorefrontFactory storefrontFactory = new(
            repositoryRoot,
            configureServices: services =>
            {
                services.AddSingleton<IOrderCheckoutClient>(new TestOrderCheckoutClient(orderHttpClient));
            });
        using HttpClient storefrontHttpClient = storefrontFactory.CreateClient();

        HttpResponseMessage response = await SendCheckoutAsync(
            storefrontHttpClient,
            "user-0001",
            "fast_success",
            "idem-052-storefront-async",
            checkoutMode: "async");

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        Assert.AreEqual("idem-052-storefront-async", response.Headers.GetValues(LabHeaderNames.IdempotencyKey).Single());

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
        string storefrontTraceId = body.GetProperty("request").GetProperty("traceId").GetString()!;
        string orderTraceId = body.GetProperty("order").GetProperty("traceId").GetString()!;
        string orderId = body.GetProperty("orderId").GetString()!;
        string paymentId = body.GetProperty("paymentId").GetString()!;
        string backgroundJobId = body.GetProperty("backgroundJobId").GetString()!;

        Assert.AreEqual("PendingPayment", body.GetProperty("status").GetString());
        Assert.IsTrue(body.GetProperty("contractSatisfied").GetBoolean());
        Assert.AreEqual("Pending", body.GetProperty("paymentStatus").GetString());
        Assert.AreEqual("async", body.GetProperty("checkoutMode").GetString());
        Assert.AreEqual("queued_for_background_confirmation", body.GetProperty("paymentOutcome").GetString());
        Assert.AreEqual("order-api", body.GetProperty("source").GetString());
        Assert.AreEqual("Order.Api", body.GetProperty("order").GetProperty("service").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(backgroundJobId));

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> requestTraces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceRecord storefrontTrace = requestTraces.Single(trace => trace.Record.TraceId == storefrontTraceId).Record;
        RequestTraceRecord orderTrace = requestTraces.Single(trace => trace.Record.TraceId == orderTraceId).Record;

        Assert.AreEqual("storefront-checkout-async", storefrontTrace.Operation);
        Assert.AreEqual("Storefront.Api", storefrontTrace.Service);
        Assert.AreEqual("/checkout", storefrontTrace.Route);
        Assert.IsTrue(storefrontTrace.ContractSatisfied);
        Assert.AreEqual(202, storefrontTrace.StatusCode);
        Assert.HasCount(1, storefrontTrace.DependencyCalls);
        Assert.AreEqual("order-api", storefrontTrace.DependencyCalls[0].DependencyName);
        Assert.AreEqual(202, storefrontTrace.DependencyCalls[0].StatusCode);
        Assert.IsFalse(storefrontTrace.DependencyCalls.Any(call => call.DependencyName == "payment-simulator"));
        Assert.AreEqual("accepted_pending", storefrontTrace.StageTimings.Single(stage => stage.StageName == "response_sent").Outcome);

        Assert.AreEqual("checkout-async", orderTrace.Operation);
        Assert.AreEqual("Order.Api", orderTrace.Service);
        Assert.IsTrue(orderTrace.ContractSatisfied);
        Assert.AreEqual(202, orderTrace.StatusCode);
        Assert.IsEmpty(orderTrace.DependencyCalls);
        Assert.AreEqual("queued", orderTrace.StageTimings.Single(stage => stage.StageName == "payment_job_enqueued").Outcome);

        await using (PrimaryDbContext dbContext = CreateDbContext(repositoryRoot))
        {
            Lab.Persistence.Entities.Order order = await dbContext.Orders.Include(item => item.Payments).SingleAsync(item => item.OrderId == orderId);
            QueueJob queueJob = await dbContext.QueueJobs.SingleAsync(item => item.QueueJobId == backgroundJobId);
            QueueJob historyQueueJob = await dbContext.QueueJobs.SingleAsync(item => item.JobType == LabQueueJobTypes.OrderHistoryProjectionUpdate);

            Assert.AreEqual("PendingPayment", order.Status);
            Assert.AreEqual("Pending", order.Payments.Single().Status);
            Assert.AreEqual(paymentId, order.Payments.Single().PaymentId);
            Assert.AreEqual(QueueJobStatuses.Pending, queueJob.Status);
            Assert.AreEqual(LabQueueJobTypes.PaymentConfirmationRetry, queueJob.JobType);
            Assert.AreEqual(QueueJobStatuses.Pending, historyQueueJob.Status);
        }

        using IHost workerHost = CreateWorkerHost(repositoryRoot, paymentHttpClient);
        WorkerQueueProcessor processor = workerHost.Services.GetRequiredService<WorkerQueueProcessor>();
        int processedTotal = 0;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            processedTotal += await processor.ProcessAvailableJobsAsync(CancellationToken.None);

            await using PrimaryDbContext dbContext = CreateDbContext(repositoryRoot);
            Lab.Persistence.Entities.Order order = await dbContext.Orders.Include(item => item.Payments).SingleAsync(item => item.OrderId == orderId);

            if (order.Status == "Paid")
            {
                Assert.AreEqual("Authorized", order.Payments.Single().Status);
                Assert.IsGreaterThanOrEqualTo(
                    1,
                    await dbContext.QueueJobs.CountAsync(item =>
                        item.JobType == LabQueueJobTypes.OrderHistoryProjectionUpdate &&
                        item.Status == QueueJobStatuses.Pending));
                break;
            }

            if (attempt == 2)
            {
                Assert.AreEqual("Paid", order.Status);
            }
        }

        Assert.IsGreaterThanOrEqualTo(1, processedTotal);

        IReadOnlyList<JobTraceTestHelper.TraceEnvelope> jobTraces = await JobTraceTestHelper.ReadJobTracesAsync(repositoryRoot);
        JobTraceRecord jobTrace = jobTraces.Single(trace => trace.Record.JobId == backgroundJobId).Record;
        Assert.AreEqual(QueueJobStatuses.Completed, jobTrace.Status);
        Assert.IsTrue(jobTrace.ContractSatisfied);
        Assert.IsTrue(jobTrace.StageTimings.Any(stage => stage.StageName == "payment_state_persisted" && stage.Outcome == "paid"));
    }

    [TestMethod]
    public async Task CheckoutEndpoint_AsyncSlowPayment_ReturnsMateriallyFasterThanSyncSlowPayment()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 3);
        await SeedActiveCartAsync(repositoryRoot, "user-0001", "sku-0001", 1);
        await SeedActiveCartAsync(repositoryRoot, "user-0002", "sku-0002", 1);

        await using PaymentSimulatorFactory paymentFactory = new(repositoryRoot, CreateSlowPaymentOverrides());
        using HttpClient paymentHttpClient = paymentFactory.CreateClient();
        await using OrderFactory orderFactory = new(
            repositoryRoot,
            configureServices: services =>
            {
                services.AddSingleton<IOrderPaymentClient>(new TestOrderPaymentClient(paymentHttpClient));
            });
        using HttpClient orderHttpClient = orderFactory.CreateClient();
        await using StorefrontFactory storefrontFactory = new(
            repositoryRoot,
            configureServices: services =>
            {
                services.AddSingleton<IOrderCheckoutClient>(new TestOrderCheckoutClient(orderHttpClient));
            });
        using HttpClient storefrontHttpClient = storefrontFactory.CreateClient();

        JsonElement syncBody = JsonSerializer.Deserialize<JsonElement>(
            await (await SendCheckoutAsync(
                    storefrontHttpClient,
                    "user-0001",
                    "slow_success",
                    "idem-052-storefront-sync",
                    checkoutMode: "sync"))
                .Content.ReadAsStringAsync(),
            JsonOptions);
        JsonElement asyncBody = JsonSerializer.Deserialize<JsonElement>(
            await (await SendCheckoutAsync(
                    storefrontHttpClient,
                    "user-0002",
                    "slow_success",
                    "idem-052-storefront-async-speed",
                    checkoutMode: "async"))
                .Content.ReadAsStringAsync(),
            JsonOptions);

        Assert.AreEqual("Paid", syncBody.GetProperty("status").GetString());
        Assert.AreEqual("sync", syncBody.GetProperty("checkoutMode").GetString());
        Assert.AreEqual("PendingPayment", asyncBody.GetProperty("status").GetString());
        Assert.AreEqual("async", asyncBody.GetProperty("checkoutMode").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(asyncBody.GetProperty("backgroundJobId").GetString()));

        string syncStorefrontTraceId = syncBody.GetProperty("request").GetProperty("traceId").GetString()!;
        string asyncStorefrontTraceId = asyncBody.GetProperty("request").GetProperty("traceId").GetString()!;
        string syncOrderTraceId = syncBody.GetProperty("order").GetProperty("traceId").GetString()!;
        string asyncOrderTraceId = asyncBody.GetProperty("order").GetProperty("traceId").GetString()!;

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceRecord syncStorefrontTrace = traces.Single(trace => trace.Record.TraceId == syncStorefrontTraceId).Record;
        RequestTraceRecord asyncStorefrontTrace = traces.Single(trace => trace.Record.TraceId == asyncStorefrontTraceId).Record;
        RequestTraceRecord syncOrderTrace = traces.Single(trace => trace.Record.TraceId == syncOrderTraceId).Record;
        RequestTraceRecord asyncOrderTrace = traces.Single(trace => trace.Record.TraceId == asyncOrderTraceId).Record;

        Assert.AreEqual("storefront-checkout-sync", syncStorefrontTrace.Operation);
        Assert.AreEqual("storefront-checkout-async", asyncStorefrontTrace.Operation);
        Assert.AreEqual("checkout-sync", syncOrderTrace.Operation);
        Assert.AreEqual("checkout-async", asyncOrderTrace.Operation);
        Assert.IsGreaterThan(asyncStorefrontTrace.LatencyMs + 50d, syncStorefrontTrace.LatencyMs);
        Assert.IsGreaterThan(asyncOrderTrace.LatencyMs + 50d, syncOrderTrace.LatencyMs);
        Assert.HasCount(1, syncOrderTrace.DependencyCalls);
        Assert.IsEmpty(asyncOrderTrace.DependencyCalls);
        Assert.AreEqual("accepted_pending", asyncStorefrontTrace.StageTimings.Single(stage => stage.StageName == "response_sent").Outcome);
    }

    [TestMethod]
    public async Task CheckoutEndpoint_WhenTokenBucketIsEmpty_Returns429AndMarksTraceAsRateLimited()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 1);
        await SeedActiveCartAsync(repositoryRoot, "user-0001", "sku-0001", 1);

        Dictionary<string, string?> overrides = new(CreateFastPaymentOverrides())
        {
            ["Lab:RateLimiter:Checkout:Enabled"] = "true",
            ["Lab:RateLimiter:Checkout:TokenBucketCapacity"] = "1",
            ["Lab:RateLimiter:Checkout:TokensPerSecond"] = "1"
        };

        await using PaymentSimulatorFactory paymentFactory = new(repositoryRoot, overrides);
        using HttpClient paymentHttpClient = paymentFactory.CreateClient();
        await using OrderFactory orderFactory = new(
            repositoryRoot,
            configurationOverrides: overrides,
            configureServices: services =>
            {
                services.AddSingleton<IOrderPaymentClient>(new TestOrderPaymentClient(paymentHttpClient));
            });
        using HttpClient orderHttpClient = orderFactory.CreateClient();
        await using StorefrontFactory storefrontFactory = new(
            repositoryRoot,
            configurationOverrides: overrides,
            configureServices: services =>
            {
                services.AddSingleton<IOrderCheckoutClient>(new TestOrderCheckoutClient(orderHttpClient));
            });
        using HttpClient storefrontHttpClient = storefrontFactory.CreateClient();

        Task<HttpResponseMessage> firstTask = SendCheckoutAsync(
            storefrontHttpClient,
            "user-0001",
            "fast_success",
            "idem-060-1",
            checkoutMode: "sync");
        Task<HttpResponseMessage> secondTask = SendCheckoutAsync(
            storefrontHttpClient,
            "user-0001",
            "fast_success",
            "idem-060-2",
            checkoutMode: "sync");

        HttpResponseMessage[] responses = await Task.WhenAll(firstTask, secondTask);
        HttpResponseMessage rejected = responses.Single(response => response.StatusCode == HttpStatusCode.TooManyRequests);
        HttpResponseMessage accepted = responses.Single(response => response.StatusCode == HttpStatusCode.OK);

        Assert.AreEqual("1", rejected.Headers.GetValues("Retry-After").Single());
        Assert.IsTrue(
            new[] { "idem-060-1", "idem-060-2" }.Contains(rejected.Headers.GetValues(LabHeaderNames.IdempotencyKey).Single(), StringComparer.Ordinal));
        Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await rejected.Content.ReadAsStringAsync(), JsonOptions);
        Assert.AreEqual("rate_limited", body.GetProperty("error").GetString());
        Assert.IsFalse(body.GetProperty("contractSatisfied").GetBoolean());
        Assert.AreEqual("user-0001", body.GetProperty("userId").GetString());

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceRecord rejectedTrace = traces
            .Select(trace => trace.Record)
            .Single(trace => trace.Service == "Storefront.Api" && trace.Operation == "storefront-checkout-sync" && trace.StatusCode == 429);

        Assert.IsTrue(rejectedTrace.RateLimited);
        Assert.IsFalse(rejectedTrace.ContractSatisfied);
        Assert.AreEqual("rate_limited", rejectedTrace.ErrorCode);
        Assert.IsEmpty(rejectedTrace.DependencyCalls);
        Assert.AreEqual("rejected", rejectedTrace.StageTimings.Single(stage => stage.StageName == "rate_limit_checked").Outcome);
        Assert.AreEqual("rate_limited", rejectedTrace.StageTimings.Single(stage => stage.StageName == "response_sent").Outcome);
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
            ["Lab:PaymentSimulator:SlowLatencyMilliseconds"] = "40",
            ["Lab:PaymentSimulator:TimeoutLatencyMilliseconds"] = "80",
            ["Lab:PaymentSimulator:DelayedConfirmationMilliseconds"] = "60",
            ["Lab:PaymentSimulator:DuplicateCallbackSpacingMilliseconds"] = "20",
            ["Lab:PaymentSimulator:DispatcherPollMilliseconds"] = "10"
        };

    private static IReadOnlyDictionary<string, string?> CreateSlowPaymentOverrides() =>
        new Dictionary<string, string?>
        {
            ["Lab:PaymentSimulator:FastLatencyMilliseconds"] = "5",
            ["Lab:PaymentSimulator:SlowLatencyMilliseconds"] = "180",
            ["Lab:PaymentSimulator:TimeoutLatencyMilliseconds"] = "220",
            ["Lab:PaymentSimulator:DelayedConfirmationMilliseconds"] = "120",
            ["Lab:PaymentSimulator:DuplicateCallbackSpacingMilliseconds"] = "30",
            ["Lab:PaymentSimulator:DispatcherPollMilliseconds"] = "10"
        };

    private static async Task SeedPrimaryDatabaseAsync(string repositoryRoot, int productCount, int userCount)
    {
        PrimaryDbContextFactory dbContextFactory = new();
        PrimaryDatabaseInitializer initializer = new(dbContextFactory);
        SqliteSeedDataService seeder = new(initializer, dbContextFactory);
        string databasePath = Path.Combine(repositoryRoot, "data", "primary.db");

        await seeder.SeedAsync(
            databasePath,
            new SeedCounts(productCount, userCount),
            resetExisting: true);
    }

    private static async Task SeedActiveCartAsync(string repositoryRoot, string userId, string productId, int quantity)
    {
        await using PrimaryDbContext dbContext = CreateDbContext(repositoryRoot);
        Product product = await dbContext.Products.SingleAsync(item => item.ProductId == productId);
        DateTimeOffset nowUtc = new(2026, 3, 31, 23, 30, 0, TimeSpan.Zero);

        CartEntity cart = new()
        {
            CartId = $"cart-{Guid.NewGuid():N}",
            UserId = userId,
            Region = "local",
            Status = "active",
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc
        };
        cart.Items.Add(new CartItemEntity
        {
            CartItemId = $"ci-{Guid.NewGuid():N}",
            CartId = cart.CartId,
            ProductId = productId,
            Quantity = quantity,
            UnitPriceCents = product.PriceCents,
            AddedUtc = nowUtc
        });

        dbContext.Carts.Add(cart);
        await dbContext.SaveChangesAsync();
    }

    private static PrimaryDbContext CreateDbContext(string repositoryRoot)
    {
        PrimaryDbContextFactory dbContextFactory = new();
        string databasePath = Path.Combine(repositoryRoot, "data", "primary.db");
        return dbContextFactory.CreateDbContext(databasePath);
    }

    private static async Task<HttpResponseMessage> SendCheckoutAsync(
        HttpClient client,
        string userId,
        string paymentMode,
        string idempotencyKey,
        string checkoutMode)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, $"/checkout?mode={Uri.EscapeDataString(checkoutMode)}")
        {
            Content = JsonContent.Create(new
            {
                userId,
                paymentMode
            })
        };
        request.Headers.TryAddWithoutValidation(LabHeaderNames.IdempotencyKey, idempotencyKey);

        return await client.SendAsync(request);
    }

    private static string CreateUniqueTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestOrderPaymentClient(HttpClient httpClient) : IOrderPaymentClient
    {
        public async Task<HttpResponseMessage> AuthorizeAsync(
            OrderPaymentAuthorizationRequest request,
            string runId,
            string correlationId,
            CancellationToken cancellationToken)
        {
            string path = string.IsNullOrWhiteSpace(request.PaymentMode)
                ? "/payments/authorize"
                : $"/payments/authorize?mode={Uri.EscapeDataString(request.PaymentMode.Trim())}";

            using HttpRequestMessage message = new(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(new
                {
                    request.PaymentId,
                    request.OrderId,
                    request.AmountCents,
                    request.Currency,
                    CallbackUrl = request.CallbackUrl
                })
            };

            message.Headers.TryAddWithoutValidation(LabHeaderNames.RunId, runId);
            message.Headers.TryAddWithoutValidation(LabHeaderNames.CorrelationId, correlationId);

            if (request.DebugTelemetryRequested)
            {
                message.Headers.TryAddWithoutValidation(LabHeaderNames.DebugTelemetry, "true");
            }

            return await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
    }

    private sealed class TestOrderCheckoutClient(HttpClient httpClient) : IOrderCheckoutClient
    {
        public async Task<HttpResponseMessage> CheckoutAsync(
            StorefrontCheckoutRequest request,
            string checkoutMode,
            string idempotencyKey,
            string runId,
            string correlationId,
            bool debugTelemetryRequested,
            CancellationToken cancellationToken)
        {
            using HttpRequestMessage message = new(
                HttpMethod.Post,
                $"/orders/checkout?mode={Uri.EscapeDataString(checkoutMode)}")
            {
                Content = JsonContent.Create(request)
            };

            message.Headers.TryAddWithoutValidation(LabHeaderNames.RunId, runId);
            message.Headers.TryAddWithoutValidation(LabHeaderNames.CorrelationId, correlationId);
            message.Headers.TryAddWithoutValidation(LabHeaderNames.IdempotencyKey, idempotencyKey);

            if (debugTelemetryRequested)
            {
                message.Headers.TryAddWithoutValidation(LabHeaderNames.DebugTelemetry, "true");
            }

            return await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
    }

    private sealed class TestServerPaymentConfirmationClient(HttpClient httpClient) : IPaymentConfirmationClient
    {
        public async Task<PaymentProviderObservation> AuthorizeAsync(
            PaymentAuthorizationCommand command,
            string runId,
            string correlationId,
            CancellationToken cancellationToken)
        {
            string path = $"/payments/authorize?mode={Uri.EscapeDataString(command.PaymentMode)}";

            using HttpRequestMessage request = new(HttpMethod.Post, path)
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

            using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return await DeserializeObservationAsync(response, cancellationToken);
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

            using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return await DeserializeObservationAsync(response, cancellationToken);
        }

        private static async Task<PaymentProviderObservation> DeserializeObservationAsync(
            HttpResponseMessage response,
            CancellationToken cancellationToken)
        {
            string payload = await response.Content.ReadAsStringAsync(cancellationToken);
            using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
            JsonElement root = document.RootElement;

            return new PaymentProviderObservation(
                StatusCode: (int)response.StatusCode,
                Outcome: ReadString(root, "outcome") ?? (response.IsSuccessStatusCode ? "unknown" : "failed"),
                ErrorCode: ReadString(root, "error"),
                ErrorDetail: ReadString(root, "detail"),
                ProviderReference: ReadString(root, "providerReference") ?? ReadString(root, "latestProviderReference"),
                CallbackPending: root.TryGetProperty("callbackPending", out JsonElement callbackPending) && callbackPending.ValueKind == JsonValueKind.True,
                CallbackCountScheduled: ReadInt(root, "callbackCountScheduled") ?? 0,
                DownstreamRunId: ReadNestedString(root, "request", "runId"),
                DownstreamTraceId: ReadNestedString(root, "request", "traceId"),
                DownstreamRequestId: ReadNestedString(root, "request", "requestId"));
        }

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
