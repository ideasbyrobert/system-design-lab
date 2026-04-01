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
using Storefront.Api.CartState;
using Storefront.Api.Checkout;
using Worker;
using Worker.DependencyInjection;
using Worker.Jobs;

namespace Lab.IntegrationTests;

[TestClass]
public sealed class EndToEndRegressionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task FullJourney_AddToCart_AsyncCheckout_WorkerCompletion_AndReadModelProjection_RemainConsistent()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);

        await using PaymentSimulatorFactory paymentFactory = new(repositoryRoot, CreateFastPaymentOverrides());
        using HttpClient paymentHttpClient = paymentFactory.CreateClient();
        await using CartFactory cartFactory = new(repositoryRoot);
        using HttpClient cartHttpClient = cartFactory.CreateClient();
        await using OrderFactory orderFactory = new(repositoryRoot);
        using HttpClient orderHttpClient = orderFactory.CreateClient();
        await using StorefrontFactory storefrontFactory = new(
            repositoryRoot,
            configureServices: services =>
            {
                services.AddSingleton<ICartClient>(new TestCartClient(cartHttpClient));
                services.AddSingleton<IOrderCheckoutClient>(new TestOrderCheckoutClient(orderHttpClient));
            });
        using HttpClient storefrontHttpClient = storefrontFactory.CreateClient();

        HttpResponseMessage addToCartResponse = await storefrontHttpClient.PostAsJsonAsync(
            "/cart/items",
            new
            {
                userId = "user-0001",
                productId = "sku-0001",
                quantity = 2
            });

        Assert.AreEqual(HttpStatusCode.OK, addToCartResponse.StatusCode);

        JsonElement addToCartBody = JsonSerializer.Deserialize<JsonElement>(await addToCartResponse.Content.ReadAsStringAsync(), JsonOptions);
        string addToCartTraceId = addToCartBody.GetProperty("request").GetProperty("traceId").GetString()!;

        Assert.AreEqual("added", addToCartBody.GetProperty("mutationOutcome").GetString());
        Assert.AreEqual(1, addToCartBody.GetProperty("distinctItemCount").GetInt32());
        Assert.AreEqual(2, addToCartBody.GetProperty("totalQuantity").GetInt32());
        Assert.AreEqual(2272, addToCartBody.GetProperty("totalPriceCents").GetInt32());

        using HttpRequestMessage checkoutRequest = new(HttpMethod.Post, "/checkout?mode=async")
        {
            Content = JsonContent.Create(new
            {
                userId = "user-0001",
                paymentMode = "fast_success"
            })
        };
        checkoutRequest.Headers.TryAddWithoutValidation(LabHeaderNames.IdempotencyKey, "idem-100-journey");

        HttpResponseMessage checkoutResponse = await storefrontHttpClient.SendAsync(checkoutRequest);

        Assert.AreEqual(HttpStatusCode.Accepted, checkoutResponse.StatusCode);
        Assert.AreEqual("idem-100-journey", checkoutResponse.Headers.GetValues(LabHeaderNames.IdempotencyKey).Single());

        JsonElement checkoutBody = JsonSerializer.Deserialize<JsonElement>(await checkoutResponse.Content.ReadAsStringAsync(), JsonOptions);
        string checkoutTraceId = checkoutBody.GetProperty("request").GetProperty("traceId").GetString()!;
        string orderId = checkoutBody.GetProperty("orderId").GetString()!;
        string paymentId = checkoutBody.GetProperty("paymentId").GetString()!;
        string backgroundJobId = checkoutBody.GetProperty("backgroundJobId").GetString()!;

        Assert.AreEqual("PendingPayment", checkoutBody.GetProperty("status").GetString());
        Assert.AreEqual("Pending", checkoutBody.GetProperty("paymentStatus").GetString());
        Assert.AreEqual("queued_for_background_confirmation", checkoutBody.GetProperty("paymentOutcome").GetString());
        Assert.AreEqual("async", checkoutBody.GetProperty("checkoutMode").GetString());
        Assert.AreEqual("order-api", checkoutBody.GetProperty("source").GetString());
        Assert.IsTrue(checkoutBody.GetProperty("contractSatisfied").GetBoolean());
        Assert.IsFalse(string.IsNullOrWhiteSpace(backgroundJobId));

        HttpResponseMessage beforeHistoryResponse = await storefrontHttpClient.GetAsync("/orders/user-0001?readSource=read-model");
        Assert.AreEqual(HttpStatusCode.OK, beforeHistoryResponse.StatusCode);

        JsonElement beforeHistoryBody = JsonSerializer.Deserialize<JsonElement>(await beforeHistoryResponse.Content.ReadAsStringAsync(), JsonOptions);
        string beforeHistoryTraceId = beforeHistoryBody.GetProperty("request").GetProperty("traceId").GetString()!;

        Assert.AreEqual("read-model", beforeHistoryBody.GetProperty("source").GetString());
        Assert.IsTrue(beforeHistoryBody.GetProperty("freshness").GetProperty("staleRead").GetBoolean());
        Assert.AreEqual(0, beforeHistoryBody.GetProperty("orderCount").GetInt32());

        using IHost workerHost = CreateWorkerHost(repositoryRoot, paymentHttpClient);
        WorkerQueueProcessor processor = workerHost.Services.GetRequiredService<WorkerQueueProcessor>();

        bool observedProjectedPaidOrder = false;
        string? afterHistoryTraceId = null;
        int processedTotal = 0;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            processedTotal += await processor.ProcessAvailableJobsAsync(CancellationToken.None);

            HttpResponseMessage historyResponse = await storefrontHttpClient.GetAsync("/orders/user-0001?readSource=read-model");
            JsonElement historyBody = JsonSerializer.Deserialize<JsonElement>(await historyResponse.Content.ReadAsStringAsync(), JsonOptions);

            afterHistoryTraceId = historyBody.GetProperty("request").GetProperty("traceId").GetString();

            if (historyBody.GetProperty("orderCount").GetInt32() == 1 &&
                !historyBody.GetProperty("freshness").GetProperty("staleRead").GetBoolean() &&
                historyBody.GetProperty("orders")[0].GetProperty("orderId").GetString() == orderId &&
                historyBody.GetProperty("orders")[0].GetProperty("status").GetString() == "Paid" &&
                historyBody.GetProperty("orders")[0].GetProperty("payment").GetProperty("status").GetString() == "Authorized")
            {
                observedProjectedPaidOrder = true;
                break;
            }
        }

        Assert.IsTrue(observedProjectedPaidOrder, "Expected Worker to complete payment confirmation and update the order-history read model.");
        Assert.IsGreaterThanOrEqualTo(1, processedTotal);
        Assert.IsFalse(string.IsNullOrWhiteSpace(afterHistoryTraceId));

        await using (PrimaryDbContext dbContext = CreateDbContext(repositoryRoot))
        {
            Lab.Persistence.Entities.Order order = await dbContext.Orders
                .Include(item => item.Payments)
                .SingleAsync(item => item.OrderId == orderId);

            Assert.AreEqual("Paid", order.Status);
            Assert.AreEqual(paymentId, order.Payments.Single().PaymentId);
            Assert.AreEqual("Authorized", order.Payments.Single().Status);
        }

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> requestTraces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        IReadOnlyList<JobTraceTestHelper.TraceEnvelope> jobTraces = await JobTraceTestHelper.ReadJobTracesAsync(repositoryRoot);

        RequestTraceRecord addToCartTrace = requestTraces.Single(trace => trace.Record.TraceId == addToCartTraceId).Record;
        RequestTraceRecord checkoutTrace = requestTraces.Single(trace => trace.Record.TraceId == checkoutTraceId).Record;
        RequestTraceRecord beforeHistoryTrace = requestTraces.Single(trace => trace.Record.TraceId == beforeHistoryTraceId).Record;
        RequestTraceRecord afterHistoryTrace = requestTraces.Single(trace => trace.Record.TraceId == afterHistoryTraceId!).Record;

        Assert.AreEqual("add-item-to-cart", addToCartTrace.Operation);
        Assert.AreEqual("storefront-checkout-async", checkoutTrace.Operation);
        Assert.AreEqual("order-history", beforeHistoryTrace.Operation);
        Assert.AreEqual("order-history", afterHistoryTrace.Operation);
        Assert.AreEqual("empty", beforeHistoryTrace.StageTimings.Single(stage => stage.StageName == "order_history_read").Outcome);
        Assert.AreEqual("loaded", afterHistoryTrace.StageTimings.Single(stage => stage.StageName == "order_history_read").Outcome);
        Assert.AreEqual(1, beforeHistoryTrace.FreshnessStaleCount);
        Assert.AreEqual(0, afterHistoryTrace.FreshnessStaleCount);

        Assert.IsTrue(requestTraces.Any(trace => trace.Record.Service == "Cart.Api" && trace.Record.Operation == "cart-add-item"));
        Assert.IsTrue(requestTraces.Any(trace => trace.Record.Service == "Order.Api" && trace.Record.Operation == "checkout-async"));
        Assert.IsTrue(requestTraces.Any(trace => trace.Record.Service == "PaymentSimulator.Api" && trace.Record.Operation == "payment-authorize"));
        Assert.IsTrue(jobTraces.Any(trace => trace.Record.JobId == backgroundJobId && trace.Record.Status == QueueJobStatuses.Completed));
        Assert.IsTrue(jobTraces.Any(trace => trace.Record.JobType == LabQueueJobTypes.OrderHistoryProjectionUpdate && trace.Record.Status == QueueJobStatuses.Completed));
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

    private static PrimaryDbContext CreateDbContext(string repositoryRoot)
    {
        PrimaryDbContextFactory dbContextFactory = new();
        string databasePath = Path.Combine(repositoryRoot, "data", "primary.db");
        return dbContextFactory.CreateDbContext(databasePath);
    }

    private static string CreateUniqueTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestCartClient(HttpClient httpClient) : ICartClient
    {
        public async Task<HttpResponseMessage> AddItemAsync(
            StorefrontCartMutationRequest request,
            string runId,
            string correlationId,
            CancellationToken cancellationToken)
        {
            using HttpRequestMessage message = new(HttpMethod.Post, "/cart/items")
            {
                Content = JsonContent.Create(request)
            };

            message.Headers.TryAddWithoutValidation(LabHeaderNames.RunId, runId);
            message.Headers.TryAddWithoutValidation(LabHeaderNames.CorrelationId, correlationId);

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
