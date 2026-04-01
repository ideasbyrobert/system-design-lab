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
using CartEntity = Lab.Persistence.Entities.Cart;
using CartItemEntity = Lab.Persistence.Entities.CartItem;

namespace Lab.IntegrationTests;

[TestClass]
public sealed class StorefrontOrderHistoryEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task OrderHistoryEndpoint_RemainsEmptyUntilWorkerAppliesProjectionUpdate()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);
        await SeedActiveCartAsync(repositoryRoot, "user-0001", "sku-0001", 1);

        await using PaymentSimulatorFactory paymentFactory = new(repositoryRoot, CreatePaymentOverrides());
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

        HttpResponseMessage checkoutResponse = await SendCheckoutAsync(
            storefrontHttpClient,
            "user-0001",
            "fast_success",
            "idem-053-history-sync",
            checkoutMode: "sync");

        Assert.AreEqual(HttpStatusCode.OK, checkoutResponse.StatusCode);

        await using (PrimaryDbContext primaryDbContext = CreateDbContext(repositoryRoot))
        {
            QueueJob queuedProjectionJob = await primaryDbContext.QueueJobs.SingleAsync();
            Assert.AreEqual(LabQueueJobTypes.OrderHistoryProjectionUpdate, queuedProjectionJob.JobType);
            Assert.AreEqual(QueueJobStatuses.Pending, queuedProjectionJob.Status);
        }

        HttpResponseMessage beforeResponse = await storefrontHttpClient.GetAsync("/orders/user-0001");
        Assert.AreEqual(HttpStatusCode.OK, beforeResponse.StatusCode);

        JsonElement beforeBody = JsonSerializer.Deserialize<JsonElement>(await beforeResponse.Content.ReadAsStringAsync(), JsonOptions);
        string beforeTraceId = beforeBody.GetProperty("request").GetProperty("traceId").GetString()!;

        Assert.AreEqual("read-model", beforeBody.GetProperty("source").GetString());
        Assert.IsTrue(beforeBody.GetProperty("freshness").GetProperty("staleRead").GetBoolean());
        Assert.AreEqual("read-model", beforeBody.GetProperty("freshness").GetProperty("readSource").GetString());
        Assert.AreEqual(0, beforeBody.GetProperty("orderCount").GetInt32());
        Assert.AreEqual(0, beforeBody.GetProperty("orders").GetArrayLength());

        using IHost workerHost = CreateWorkerHost(repositoryRoot);
        WorkerQueueProcessor processor = workerHost.Services.GetRequiredService<WorkerQueueProcessor>();
        Assert.AreEqual(1, await processor.ProcessAvailableJobsAsync(CancellationToken.None));

        HttpResponseMessage afterResponse = await storefrontHttpClient.GetAsync("/orders/user-0001");
        Assert.AreEqual(HttpStatusCode.OK, afterResponse.StatusCode);

        JsonElement afterBody = JsonSerializer.Deserialize<JsonElement>(await afterResponse.Content.ReadAsStringAsync(), JsonOptions);
        string afterTraceId = afterBody.GetProperty("request").GetProperty("traceId").GetString()!;
        JsonElement order = afterBody.GetProperty("orders")[0];
        JsonElement payment = order.GetProperty("payment");
        JsonElement item = order.GetProperty("items")[0];

        Assert.AreEqual("read-model", afterBody.GetProperty("source").GetString());
        Assert.IsFalse(afterBody.GetProperty("freshness").GetProperty("staleRead").GetBoolean());
        Assert.AreEqual(1, afterBody.GetProperty("orderCount").GetInt32());
        Assert.AreEqual("user-0001", afterBody.GetProperty("userId").GetString());
        Assert.IsTrue(afterBody.TryGetProperty("newestProjectedUtc", out JsonElement newestProjectedUtc));
        Assert.AreEqual("Paid", order.GetProperty("status").GetString());
        Assert.AreEqual("Authorized", payment.GetProperty("status").GetString());
        Assert.AreEqual("sku-0001", item.GetProperty("productId").GetString());
        Assert.AreEqual("Sample Product 0001", item.GetProperty("productName").GetString());

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceRecord beforeTrace = traces.Single(trace => trace.Record.TraceId == beforeTraceId).Record;
        RequestTraceRecord afterTrace = traces.Single(trace => trace.Record.TraceId == afterTraceId).Record;

        Assert.AreEqual("order-history", beforeTrace.Operation);
        Assert.AreEqual("Storefront.Api", beforeTrace.Service);
        Assert.IsTrue(beforeTrace.ContractSatisfied);
        Assert.IsEmpty(beforeTrace.DependencyCalls);
        Assert.AreEqual("read-model", beforeTrace.ReadSource);
        Assert.AreEqual(1, beforeTrace.FreshnessComparedCount);
        Assert.AreEqual(1, beforeTrace.FreshnessStaleCount);
        Assert.AreEqual("empty", beforeTrace.StageTimings.Single(stage => stage.StageName == "order_history_read").Outcome);

        Assert.AreEqual("order-history", afterTrace.Operation);
        Assert.AreEqual("Storefront.Api", afterTrace.Service);
        Assert.IsTrue(afterTrace.ContractSatisfied);
        Assert.IsEmpty(afterTrace.DependencyCalls);
        Assert.AreEqual("read-model", afterTrace.ReadSource);
        Assert.AreEqual(1, afterTrace.FreshnessComparedCount);
        Assert.AreEqual(0, afterTrace.FreshnessStaleCount);
        Assert.AreEqual("loaded", afterTrace.StageTimings.Single(stage => stage.StageName == "order_history_read").Outcome);
        Assert.AreEqual("read-model", beforeTrace.StageTimings.Single(stage => stage.StageName == "order_history_read").Metadata["source"]);
        Assert.AreEqual("read-model", afterTrace.StageTimings.Single(stage => stage.StageName == "order_history_read").Metadata["source"]);
    }

    [TestMethod]
    public async Task OrderHistoryEndpoint_ReadSourceCanSwitchBetweenPrimaryProjectionAndLaggedReadModel()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);
        await SeedActiveCartAsync(repositoryRoot, "user-0001", "sku-0001", 1);

        await using PaymentSimulatorFactory paymentFactory = new(repositoryRoot, CreatePaymentOverrides());
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

        HttpResponseMessage checkoutResponse = await SendCheckoutAsync(
            storefrontHttpClient,
            "user-0001",
            "fast_success",
            "idem-081-history-source",
            checkoutMode: "sync");

        Assert.AreEqual(HttpStatusCode.OK, checkoutResponse.StatusCode);

        HttpResponseMessage readModelResponse = await storefrontHttpClient.GetAsync("/orders/user-0001?readSource=read-model");
        HttpResponseMessage primaryProjectionResponse = await storefrontHttpClient.GetAsync("/orders/user-0001?readSource=primary-projection");

        Assert.AreEqual(HttpStatusCode.OK, readModelResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, primaryProjectionResponse.StatusCode);

        JsonElement readModelBody = JsonSerializer.Deserialize<JsonElement>(await readModelResponse.Content.ReadAsStringAsync(), JsonOptions);
        JsonElement primaryProjectionBody = JsonSerializer.Deserialize<JsonElement>(await primaryProjectionResponse.Content.ReadAsStringAsync(), JsonOptions);

        Assert.AreEqual("read-model", readModelBody.GetProperty("source").GetString());
        Assert.IsTrue(readModelBody.GetProperty("freshness").GetProperty("staleRead").GetBoolean());
        Assert.AreEqual(0, readModelBody.GetProperty("orderCount").GetInt32());
        Assert.AreEqual("primary-projection", primaryProjectionBody.GetProperty("source").GetString());
        Assert.IsFalse(primaryProjectionBody.GetProperty("freshness").GetProperty("staleRead").GetBoolean());
        Assert.AreEqual(1, primaryProjectionBody.GetProperty("orderCount").GetInt32());
        Assert.AreEqual("Paid", primaryProjectionBody.GetProperty("orders")[0].GetProperty("status").GetString());

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceRecord readModelTrace = traces.Single(trace => trace.Record.TraceId == readModelBody.GetProperty("request").GetProperty("traceId").GetString()).Record;
        RequestTraceRecord primaryProjectionTrace = traces.Single(trace => trace.Record.TraceId == primaryProjectionBody.GetProperty("request").GetProperty("traceId").GetString()).Record;

        Assert.AreEqual("read-model", readModelTrace.StageTimings.Single(stage => stage.StageName == "order_history_read").Metadata["source"]);
        Assert.AreEqual("primary-projection", primaryProjectionTrace.StageTimings.Single(stage => stage.StageName == "order_history_read").Metadata["source"]);
        Assert.AreEqual("read-model", readModelTrace.ReadSource);
        Assert.AreEqual(1, readModelTrace.FreshnessComparedCount);
        Assert.AreEqual(1, readModelTrace.FreshnessStaleCount);
        Assert.AreEqual("primary-projection", primaryProjectionTrace.ReadSource);
        Assert.AreEqual(1, primaryProjectionTrace.FreshnessComparedCount);
        Assert.AreEqual(0, primaryProjectionTrace.FreshnessStaleCount);
        Assert.AreEqual("empty", readModelTrace.StageTimings.Single(stage => stage.StageName == "order_history_read").Outcome);
        Assert.AreEqual("loaded", primaryProjectionTrace.StageTimings.Single(stage => stage.StageName == "order_history_read").Outcome);
    }

    [TestMethod]
    public async Task OrderHistoryEndpoint_LocalRead_FallsBackToPrimaryProjectionWhenReadModelIsInvalid()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);
        await SeedActiveCartAsync(repositoryRoot, "user-0001", "sku-0001", 1);

        await using PaymentSimulatorFactory paymentFactory = new(repositoryRoot, CreatePaymentOverrides());
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
            configurationOverrides: new Dictionary<string, string?>
            {
                [$"{LabConfigurationSections.Regions}:CurrentRegion"] = "us-west",
                [$"{LabConfigurationSections.Regions}:PrimaryRegion"] = "us-east"
            },
            configureServices: services =>
            {
                services.AddSingleton<IOrderCheckoutClient>(new TestOrderCheckoutClient(orderHttpClient));
            });
        using HttpClient storefrontHttpClient = storefrontFactory.CreateClient();

        HttpResponseMessage checkoutResponse = await SendCheckoutAsync(
            storefrontHttpClient,
            "user-0001",
            "fast_success",
            "idem-091-history-local-fallback",
            checkoutMode: "sync");

        Assert.AreEqual(HttpStatusCode.OK, checkoutResponse.StatusCode);

        using IHost workerHost = CreateWorkerHost(repositoryRoot);
        WorkerQueueProcessor processor = workerHost.Services.GetRequiredService<WorkerQueueProcessor>();
        Assert.AreEqual(1, await processor.ProcessAvailableJobsAsync(CancellationToken.None));

        await CorruptOrderHistoryReadModelAsync(repositoryRoot, "user-0001");

        HttpResponseMessage response = await storefrontHttpClient.GetAsync("/orders/user-0001?readSource=local");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
        string traceId = body.GetProperty("request").GetProperty("traceId").GetString()!;

        Assert.AreEqual("primary-projection", body.GetProperty("source").GetString());
        Assert.AreEqual(1, body.GetProperty("orderCount").GetInt32());
        Assert.AreEqual("Paid", body.GetProperty("orders")[0].GetProperty("status").GetString());
        Assert.IsFalse(body.GetProperty("freshness").GetProperty("staleRead").GetBoolean());

        RequestTraceRecord trace = (await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot))
            .Single(item => item.Record.TraceId == traceId)
            .Record;

        StageTimingRecord orderHistoryReadStage = trace.StageTimings.Single(stage => stage.StageName == "order_history_read");
        Assert.AreEqual("local", orderHistoryReadStage.Metadata["requestedReadSource"]);
        Assert.AreEqual("primary-projection", orderHistoryReadStage.Metadata["effectiveReadSource"]);
        Assert.AreEqual("cross-region", orderHistoryReadStage.Metadata["selectionScope"]);
        Assert.AreEqual("us-east", orderHistoryReadStage.Metadata["targetRegion"]);
        Assert.AreEqual("true", orderHistoryReadStage.Metadata["fallbackApplied"]);
        Assert.AreEqual("read_model_invalid", orderHistoryReadStage.Metadata["fallbackReason"]);
        Assert.AreEqual("primary-projection", trace.ReadSource);
    }

    private static IHost CreateWorkerHost(string repositoryRoot)
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

        return builder.Build();
    }

    private static IReadOnlyDictionary<string, string?> CreatePaymentOverrides() =>
        new Dictionary<string, string?>
        {
            ["Lab:PaymentSimulator:FastLatencyMilliseconds"] = "5",
            ["Lab:PaymentSimulator:SlowLatencyMilliseconds"] = "30",
            ["Lab:PaymentSimulator:TimeoutLatencyMilliseconds"] = "60"
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

    private static async Task SeedActiveCartAsync(
        string repositoryRoot,
        string userId,
        string productId,
        int quantity)
    {
        await using PrimaryDbContext dbContext = CreateDbContext(repositoryRoot);

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        CartEntity cart = new()
        {
            CartId = $"cart-{Guid.NewGuid():N}",
            UserId = userId,
            Region = "local",
            Status = "active",
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc
        };

        dbContext.Carts.Add(cart);
        dbContext.CartItems.Add(new CartItemEntity
        {
            CartItemId = $"ci-{Guid.NewGuid():N}",
            CartId = cart.CartId,
            ProductId = productId,
            Quantity = quantity,
            UnitPriceCents = await dbContext.Products
                .Where(item => item.ProductId == productId)
                .Select(item => item.PriceCents)
                .SingleAsync(),
            AddedUtc = nowUtc
        });

        await dbContext.SaveChangesAsync();
    }

    private static PrimaryDbContext CreateDbContext(string repositoryRoot)
    {
        PrimaryDbContextFactory dbContextFactory = new();
        return dbContextFactory.CreateDbContext(Path.Combine(repositoryRoot, "data", "primary.db"));
    }

    private static async Task CorruptOrderHistoryReadModelAsync(string repositoryRoot, string userId)
    {
        ReadModelDbContextFactory dbContextFactory = new();
        await using ReadModelDbContext dbContext = dbContextFactory.CreateDbContext(Path.Combine(repositoryRoot, "data", "readmodels.db"));
        ReadModelOrderHistory projection = await dbContext.OrderHistories.SingleAsync(item => item.UserId == userId);
        projection.SummaryJson = "{ invalid-json";
        await dbContext.SaveChangesAsync();
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
            HttpRequestMessage message = new(HttpMethod.Post, "/payments/authorize")
            {
                Content = JsonContent.Create(new
                {
                    request.PaymentId,
                    request.OrderId,
                    request.AmountCents,
                    request.Currency
                })
            };

            if (!string.IsNullOrWhiteSpace(request.PaymentMode))
            {
                message.RequestUri = new Uri($"/payments/authorize?mode={Uri.EscapeDataString(request.PaymentMode)}", UriKind.Relative);
            }

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
            HttpRequestMessage message = new(HttpMethod.Post, $"/orders/checkout?mode={Uri.EscapeDataString(checkoutMode)}")
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
}
