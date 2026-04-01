using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lab.Persistence;
using Lab.Persistence.Entities;
using Lab.Persistence.Queueing;
using Lab.Persistence.Seeding;
using Lab.Shared.Http;
using Lab.Shared.Queueing;
using Lab.Telemetry.RequestTracing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Order.Api.Checkout;
using CartEntity = Lab.Persistence.Entities.Cart;
using CartItemEntity = Lab.Persistence.Entities.CartItem;
using OrderEntity = Lab.Persistence.Entities.Order;

namespace Lab.IntegrationTests;

[TestClass]
public sealed class OrderCheckoutEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task Checkout_FastSuccess_ProducesPaidOrder_AndPersistsAuthorizedPayment()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);
        await SeedActiveCartAsync(repositoryRoot, "user-0001", "sku-0001", 2);

        await using PrimaryDbContext beforeContext = CreateDbContext(repositoryRoot);
        InventoryRecord inventoryBefore = await beforeContext.Inventory.AsNoTracking().SingleAsync(item => item.ProductId == "sku-0001");

        await using PaymentSimulatorFactory paymentFactory = new(repositoryRoot, CreatePaymentTestOverrides());
        using HttpClient paymentHttpClient = paymentFactory.CreateClient();
        await using OrderFactory orderFactory = new(
            repositoryRoot,
            configureServices: services =>
            {
                services.AddSingleton<IOrderPaymentClient>(new TestOrderPaymentClient(paymentHttpClient));
            });
        using HttpClient orderHttpClient = orderFactory.CreateClient();

        HttpResponseMessage response = await SendCheckoutAsync(orderHttpClient, "user-0001", "fast_success", "idem-fast-042");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("idem-fast-042", response.Headers.GetValues(LabHeaderNames.IdempotencyKey).Single());

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
        string orderTraceId = body.GetProperty("request").GetProperty("traceId").GetString()!;

        Assert.AreEqual("Paid", body.GetProperty("status").GetString());
        Assert.IsTrue(body.GetProperty("contractSatisfied").GetBoolean());
        Assert.AreEqual("Authorized", body.GetProperty("paymentStatus").GetString());
        Assert.AreEqual("fast_success", body.GetProperty("paymentMode").GetString());
        Assert.AreEqual(2272, body.GetProperty("totalAmountCents").GetInt32());
        Assert.IsFalse(string.IsNullOrWhiteSpace(body.GetProperty("orderId").GetString()));
        Assert.IsFalse(string.IsNullOrWhiteSpace(body.GetProperty("paymentId").GetString()));
        Assert.IsFalse(string.IsNullOrWhiteSpace(body.GetProperty("paymentProviderReference").GetString()));
        Assert.AreEqual("authorized", body.GetProperty("paymentOutcome").GetString());

        await using PrimaryDbContext dbContext = CreateDbContext(repositoryRoot);
        OrderEntity order = await dbContext.Orders
            .Include(item => item.Payments)
            .Include(item => item.Items)
            .SingleAsync(item => item.OrderId == body.GetProperty("orderId").GetString());
        QueueJob historyQueueJob = await dbContext.QueueJobs.SingleAsync(item => item.JobType == LabQueueJobTypes.OrderHistoryProjectionUpdate);

        Assert.AreEqual("Paid", order.Status);
        Assert.AreEqual(2272, order.TotalPriceCents);
        Assert.HasCount(1, order.Payments);
        Assert.AreEqual("Authorized", order.Payments.Single().Status);
        Assert.AreEqual(body.GetProperty("paymentProviderReference").GetString(), order.Payments.Single().ExternalReference);
        Assert.IsNotNull(order.Payments.Single().ConfirmedUtc);
        Assert.HasCount(1, order.Items);
        Assert.AreEqual(QueueJobStatuses.Pending, historyQueueJob.Status);

        InventoryRecord inventory = await dbContext.Inventory.AsNoTracking().SingleAsync(item => item.ProductId == "sku-0001");
        Assert.AreEqual(inventoryBefore.AvailableQuantity - 2, inventory.AvailableQuantity);
        Assert.AreEqual(inventoryBefore.ReservedQuantity + 2, inventory.ReservedQuantity);

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceRecord orderTrace = traces.Single(trace => trace.Record.TraceId == orderTraceId).Record;
        RequestTraceRecord paymentTrace = traces.Select(trace => trace.Record).Single(trace => trace.Service == "PaymentSimulator.Api");

        Assert.AreEqual("checkout-sync", orderTrace.Operation);
        Assert.AreEqual("Order.Api", orderTrace.Service);
        Assert.AreEqual("/orders/checkout", orderTrace.Route);
        Assert.AreEqual("user-0001", orderTrace.UserId);
        Assert.IsTrue(orderTrace.ContractSatisfied);
        Assert.AreEqual(200, orderTrace.StatusCode);
        Assert.HasCount(1, orderTrace.DependencyCalls);
        Assert.AreEqual("payment-simulator", orderTrace.DependencyCalls[0].DependencyName);
        Assert.AreEqual(200, orderTrace.DependencyCalls[0].StatusCode);
        Assert.AreEqual("success", orderTrace.DependencyCalls[0].Outcome);
        Assert.AreEqual("fast_success", orderTrace.DependencyCalls[0].Metadata["paymentMode"]);
        Assert.AreEqual("authorized", orderTrace.DependencyCalls[0].Metadata["paymentOutcome"]);
        Assert.AreEqual(paymentTrace.TraceId, orderTrace.DependencyCalls[0].Metadata["downstreamTraceId"]);
        Assert.AreEqual("loaded", orderTrace.StageTimings.Single(stage => stage.StageName == "cart_loaded").Outcome);
        Assert.AreEqual("reserved", orderTrace.StageTimings.Single(stage => stage.StageName == "inventory_reserved").Outcome);
        Assert.AreEqual("authorized", orderTrace.StageTimings.Single(stage => stage.StageName == "payment_request_completed").Outcome);
        Assert.AreEqual("paid", orderTrace.StageTimings.Single(stage => stage.StageName == "order_persisted").Outcome);
        Assert.AreEqual("paid", orderTrace.StageTimings.Single(stage => stage.StageName == "response_sent").Outcome);

        Assert.AreEqual("payment-authorize", paymentTrace.Operation);
        Assert.AreEqual(orderTrace.CorrelationId, paymentTrace.CorrelationId);
    }

    [TestMethod]
    public async Task Checkout_AsyncMode_ReturnsAcceptedPending_AndEnqueuesBackgroundJob()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);
        await SeedActiveCartAsync(repositoryRoot, "user-0001", "sku-0001", 2);

        await using OrderFactory orderFactory = new(repositoryRoot);
        using HttpClient orderHttpClient = orderFactory.CreateClient();

        HttpResponseMessage response = await SendCheckoutAsync(
            orderHttpClient,
            "user-0001",
            "slow_success",
            "idem-052-async-order",
            checkoutMode: "async");

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        Assert.AreEqual("idem-052-async-order", response.Headers.GetValues(LabHeaderNames.IdempotencyKey).Single());

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
        string traceId = body.GetProperty("request").GetProperty("traceId").GetString()!;
        string orderId = body.GetProperty("orderId").GetString()!;
        string paymentId = body.GetProperty("paymentId").GetString()!;
        string backgroundJobId = body.GetProperty("backgroundJobId").GetString()!;

        Assert.AreEqual("PendingPayment", body.GetProperty("status").GetString());
        Assert.IsTrue(body.GetProperty("contractSatisfied").GetBoolean());
        Assert.AreEqual("Pending", body.GetProperty("paymentStatus").GetString());
        Assert.AreEqual("slow_success", body.GetProperty("paymentMode").GetString());
        Assert.AreEqual("queued_for_background_confirmation", body.GetProperty("paymentOutcome").GetString());
        Assert.AreEqual("async", body.GetProperty("checkoutMode").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(backgroundJobId));

        await using PrimaryDbContext dbContext = CreateDbContext(repositoryRoot);
        OrderEntity order = await dbContext.Orders
            .Include(item => item.Payments)
            .SingleAsync(item => item.OrderId == orderId);
        QueueJob paymentQueueJob = await dbContext.QueueJobs.SingleAsync(item => item.QueueJobId == backgroundJobId);
        QueueJob historyQueueJob = await dbContext.QueueJobs.SingleAsync(item => item.JobType == LabQueueJobTypes.OrderHistoryProjectionUpdate);

        Assert.AreEqual("PendingPayment", order.Status);
        Assert.HasCount(1, order.Payments);
        Assert.AreEqual(paymentId, order.Payments.Single().PaymentId);
        Assert.AreEqual("Pending", order.Payments.Single().Status);
        Assert.AreEqual("slow_success", order.Payments.Single().Mode);
        Assert.AreEqual(2, await dbContext.QueueJobs.CountAsync());
        Assert.AreEqual(QueueJobStatuses.Pending, paymentQueueJob.Status);
        Assert.AreEqual(LabQueueJobTypes.PaymentConfirmationRetry, paymentQueueJob.JobType);
        Assert.AreEqual(QueueJobStatuses.Pending, historyQueueJob.Status);

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceRecord orderTrace = traces.Single(trace => trace.Record.TraceId == traceId).Record;

        Assert.AreEqual("checkout-async", orderTrace.Operation);
        Assert.AreEqual("Order.Api", orderTrace.Service);
        Assert.AreEqual("/orders/checkout", orderTrace.Route);
        Assert.IsTrue(orderTrace.ContractSatisfied);
        Assert.AreEqual(202, orderTrace.StatusCode);
        Assert.IsEmpty(orderTrace.DependencyCalls);
        Assert.AreEqual("queued", orderTrace.StageTimings.Single(stage => stage.StageName == "payment_job_enqueued").Outcome);
        Assert.AreEqual("pending_payment", orderTrace.StageTimings.Single(stage => stage.StageName == "order_persisted").Outcome);
        Assert.AreEqual("accepted_pending", orderTrace.StageTimings.Single(stage => stage.StageName == "response_sent").Outcome);
        CollectionAssert.Contains(
            orderTrace.Notes.ToArray(),
            "Asynchronous checkout accepted the order and delegated payment confirmation to Worker.");
    }

    [TestMethod]
    public async Task Checkout_WithoutIdempotencyKey_FailsValidation_WithoutCreatingOrderOrPayment()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);
        await SeedActiveCartAsync(repositoryRoot, "user-0001", "sku-0001", 1);

        await using OrderFactory orderFactory = new(repositoryRoot);
        using HttpClient orderHttpClient = orderFactory.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Post, "/orders/checkout")
        {
            Content = JsonContent.Create(new
            {
                userId = "user-0001",
                paymentMode = "fast_success"
            })
        };

        HttpResponseMessage response = await orderHttpClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
        string traceId = body.GetProperty("request").GetProperty("traceId").GetString()!;

        Assert.AreEqual("missing_idempotency_key", body.GetProperty("error").GetString());
        Assert.IsFalse(body.GetProperty("contractSatisfied").GetBoolean());
        StringAssert.Contains(body.GetProperty("detail").GetString(), LabHeaderNames.IdempotencyKey);

        await using PrimaryDbContext dbContext = CreateDbContext(repositoryRoot);
        Assert.AreEqual(0, await dbContext.Orders.CountAsync());
        Assert.AreEqual(0, await dbContext.Payments.CountAsync());

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceRecord orderTrace = traces.Single(trace => trace.Record.TraceId == traceId).Record;

        Assert.AreEqual("validation_failed", orderTrace.StageTimings.Single(stage => stage.StageName == "response_sent").Outcome);
        Assert.AreEqual("missing_idempotency_key", orderTrace.ErrorCode);
        Assert.IsEmpty(orderTrace.DependencyCalls);
    }

    [TestMethod]
    public async Task Checkout_Timeout_ProducesFailedOrder_AndPersistsTimedOutPayment()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);
        await SeedActiveCartAsync(repositoryRoot, "user-0002", "sku-0002", 1);

        await using PaymentSimulatorFactory paymentFactory = new(repositoryRoot, CreatePaymentTestOverrides());
        using HttpClient paymentHttpClient = paymentFactory.CreateClient();
        await using OrderFactory orderFactory = new(
            repositoryRoot,
            configureServices: services =>
            {
                services.AddSingleton<IOrderPaymentClient>(new TestOrderPaymentClient(paymentHttpClient));
            });
        using HttpClient orderHttpClient = orderFactory.CreateClient();

        HttpResponseMessage response = await SendCheckoutAsync(orderHttpClient, "user-0002", "timeout", "idem-timeout-042");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("idem-timeout-042", response.Headers.GetValues(LabHeaderNames.IdempotencyKey).Single());

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
        string orderTraceId = body.GetProperty("request").GetProperty("traceId").GetString()!;

        Assert.AreEqual("Failed", body.GetProperty("status").GetString());
        Assert.IsTrue(body.GetProperty("contractSatisfied").GetBoolean());
        Assert.AreEqual("Timeout", body.GetProperty("paymentStatus").GetString());
        Assert.AreEqual("timeout", body.GetProperty("paymentMode").GetString());
        Assert.AreEqual("timeout", body.GetProperty("paymentOutcome").GetString());
        Assert.AreEqual("simulated_timeout", body.GetProperty("paymentErrorCode").GetString());

        await using PrimaryDbContext dbContext = CreateDbContext(repositoryRoot);
        OrderEntity order = await dbContext.Orders
            .Include(item => item.Payments)
            .SingleAsync(item => item.OrderId == body.GetProperty("orderId").GetString());
        QueueJob historyQueueJob = await dbContext.QueueJobs.SingleAsync(item => item.JobType == LabQueueJobTypes.OrderHistoryProjectionUpdate);

        Assert.AreEqual("Failed", order.Status);
        Assert.HasCount(1, order.Payments);
        Assert.AreEqual("Timeout", order.Payments.Single().Status);
        Assert.AreEqual(body.GetProperty("paymentProviderReference").GetString(), order.Payments.Single().ExternalReference);
        Assert.IsNull(order.Payments.Single().ConfirmedUtc);
        Assert.AreEqual(QueueJobStatuses.Pending, historyQueueJob.Status);

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceRecord orderTrace = traces.Single(trace => trace.Record.TraceId == orderTraceId).Record;
        RequestTraceRecord paymentTrace = traces.Select(trace => trace.Record).Single(trace => trace.Service == "PaymentSimulator.Api");

        Assert.IsTrue(orderTrace.ContractSatisfied);
        Assert.AreEqual("timeout", orderTrace.StageTimings.Single(stage => stage.StageName == "payment_request_completed").Outcome);
        Assert.AreEqual("failed", orderTrace.StageTimings.Single(stage => stage.StageName == "order_persisted").Outcome);
        Assert.AreEqual("failed", orderTrace.StageTimings.Single(stage => stage.StageName == "response_sent").Outcome);
        Assert.AreEqual("simulated_timeout", orderTrace.ErrorCode);
        Assert.AreEqual(504, orderTrace.DependencyCalls.Single().StatusCode);
        Assert.AreEqual("failed", orderTrace.DependencyCalls.Single().Outcome);
        CollectionAssert.Contains(orderTrace.Notes.ToArray(), "Synchronous checkout chose the explicit failed-order rule for non-successful payment outcomes.");

        Assert.AreEqual("simulated_timeout", paymentTrace.ErrorCode);
        Assert.AreEqual(orderTrace.CorrelationId, paymentTrace.CorrelationId);
    }

    [TestMethod]
    public async Task Checkout_WithSameIdempotencyKey_ReplaysOriginalResult_WithoutSecondCharge()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);
        await SeedActiveCartAsync(repositoryRoot, "user-0001", "sku-0001", 2);

        await using PrimaryDbContext beforeContext = CreateDbContext(repositoryRoot);
        InventoryRecord inventoryBefore = await beforeContext.Inventory.AsNoTracking().SingleAsync(item => item.ProductId == "sku-0001");

        await using PaymentSimulatorFactory paymentFactory = new(repositoryRoot, CreatePaymentTestOverrides());
        using HttpClient paymentHttpClient = paymentFactory.CreateClient();
        await using OrderFactory orderFactory = new(
            repositoryRoot,
            configureServices: services =>
            {
                services.AddSingleton<IOrderPaymentClient>(new TestOrderPaymentClient(paymentHttpClient));
            });
        using HttpClient orderHttpClient = orderFactory.CreateClient();

        HttpResponseMessage firstResponse = await SendCheckoutAsync(orderHttpClient, "user-0001", "fast_success", "idem-043-replay");
        HttpResponseMessage secondResponse = await SendCheckoutAsync(orderHttpClient, "user-0001", "fast_success", "idem-043-replay");

        Assert.AreEqual(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, secondResponse.StatusCode);

        JsonElement firstBody = JsonSerializer.Deserialize<JsonElement>(await firstResponse.Content.ReadAsStringAsync(), JsonOptions);
        JsonElement secondBody = JsonSerializer.Deserialize<JsonElement>(await secondResponse.Content.ReadAsStringAsync(), JsonOptions);
        string firstTraceId = firstBody.GetProperty("request").GetProperty("traceId").GetString()!;
        string secondTraceId = secondBody.GetProperty("request").GetProperty("traceId").GetString()!;

        Assert.AreEqual(firstBody.GetProperty("orderId").GetString(), secondBody.GetProperty("orderId").GetString());
        Assert.AreEqual(firstBody.GetProperty("paymentId").GetString(), secondBody.GetProperty("paymentId").GetString());
        Assert.AreEqual(firstBody.GetProperty("paymentProviderReference").GetString(), secondBody.GetProperty("paymentProviderReference").GetString());
        Assert.AreEqual("Paid", secondBody.GetProperty("status").GetString());
        Assert.IsTrue(secondBody.GetProperty("contractSatisfied").GetBoolean());
        Assert.AreEqual("Authorized", secondBody.GetProperty("paymentStatus").GetString());

        await using PrimaryDbContext dbContext = CreateDbContext(repositoryRoot);
        Assert.AreEqual(1, await dbContext.Orders.CountAsync());
        Assert.AreEqual(1, await dbContext.Payments.CountAsync());
        InventoryRecord inventoryAfter = await dbContext.Inventory.AsNoTracking().SingleAsync(item => item.ProductId == "sku-0001");
        Assert.AreEqual(inventoryBefore.AvailableQuantity - 2, inventoryAfter.AvailableQuantity);
        Assert.AreEqual(inventoryBefore.ReservedQuantity + 2, inventoryAfter.ReservedQuantity);

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceRecord[] orderTraces = traces
            .Where(trace => trace.Record.Service == "Order.Api")
            .Select(trace => trace.Record)
            .ToArray();
        RequestTraceRecord[] paymentTraces = traces
            .Where(trace => trace.Record.Service == "PaymentSimulator.Api")
            .Select(trace => trace.Record)
            .ToArray();

        Assert.HasCount(2, orderTraces);
        Assert.HasCount(1, paymentTraces);

        RequestTraceRecord replayTrace = orderTraces.Single(trace => trace.TraceId == secondTraceId);
        Assert.AreEqual("hit", replayTrace.StageTimings.Single(stage => stage.StageName == "idempotency_checked").Outcome);
        Assert.AreEqual("reused", replayTrace.StageTimings.Single(stage => stage.StageName == "inventory_reserved").Outcome);
        Assert.AreEqual("reused", replayTrace.StageTimings.Single(stage => stage.StageName == "payment_request_started").Outcome);
        Assert.AreEqual("reused", replayTrace.StageTimings.Single(stage => stage.StageName == "payment_request_completed").Outcome);
        Assert.AreEqual("reused", replayTrace.StageTimings.Single(stage => stage.StageName == "order_persisted").Outcome);
        Assert.IsEmpty(replayTrace.DependencyCalls);
        CollectionAssert.Contains(
            replayTrace.Notes.ToArray(),
            "Order.Api reused the persisted checkout result for the existing idempotency key and skipped inventory reservation plus payment authorization.");

        RequestTraceRecord originalTrace = orderTraces.Single(trace => trace.TraceId == firstTraceId);
        Assert.HasCount(1, originalTrace.DependencyCalls);
    }

    [TestMethod]
    public async Task Checkout_WithDifferentIdempotencyKeys_CreatesDistinctOrdersAndPaymentAttempts()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);
        await SeedActiveCartAsync(repositoryRoot, "user-0001", "sku-0001", 1);

        await using PrimaryDbContext beforeContext = CreateDbContext(repositoryRoot);
        InventoryRecord inventoryBefore = await beforeContext.Inventory.AsNoTracking().SingleAsync(item => item.ProductId == "sku-0001");

        await using PaymentSimulatorFactory paymentFactory = new(repositoryRoot, CreatePaymentTestOverrides());
        using HttpClient paymentHttpClient = paymentFactory.CreateClient();
        await using OrderFactory orderFactory = new(
            repositoryRoot,
            configureServices: services =>
            {
                services.AddSingleton<IOrderPaymentClient>(new TestOrderPaymentClient(paymentHttpClient));
            });
        using HttpClient orderHttpClient = orderFactory.CreateClient();

        JsonElement firstBody = JsonSerializer.Deserialize<JsonElement>(await (await SendCheckoutAsync(orderHttpClient, "user-0001", "fast_success", "idem-043-a")).Content.ReadAsStringAsync(), JsonOptions);
        JsonElement secondBody = JsonSerializer.Deserialize<JsonElement>(await (await SendCheckoutAsync(orderHttpClient, "user-0001", "fast_success", "idem-043-b")).Content.ReadAsStringAsync(), JsonOptions);

        Assert.AreNotEqual(firstBody.GetProperty("orderId").GetString(), secondBody.GetProperty("orderId").GetString());
        Assert.AreNotEqual(firstBody.GetProperty("paymentId").GetString(), secondBody.GetProperty("paymentId").GetString());
        Assert.AreNotEqual(firstBody.GetProperty("paymentProviderReference").GetString(), secondBody.GetProperty("paymentProviderReference").GetString());

        await using PrimaryDbContext dbContext = CreateDbContext(repositoryRoot);
        Assert.AreEqual(2, await dbContext.Orders.CountAsync());
        Assert.AreEqual(2, await dbContext.Payments.CountAsync());
        InventoryRecord inventoryAfter = await dbContext.Inventory.AsNoTracking().SingleAsync(item => item.ProductId == "sku-0001");
        Assert.AreEqual(inventoryBefore.AvailableQuantity - 2, inventoryAfter.AvailableQuantity);
        Assert.AreEqual(inventoryBefore.ReservedQuantity + 2, inventoryAfter.ReservedQuantity);

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        Assert.AreEqual(2, traces.Count(trace => trace.Record.Service == "PaymentSimulator.Api"));
    }

    [TestMethod]
    public async Task Checkout_TransientFailureRetriedWithSameIdempotencyKey_ReturnsOriginalFailureWithoutSecondPaymentAttempt()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);
        await SeedActiveCartAsync(repositoryRoot, "user-0001", "sku-0001", 1);

        await using PaymentSimulatorFactory paymentFactory = new(repositoryRoot, CreatePaymentTestOverrides());
        using HttpClient paymentHttpClient = paymentFactory.CreateClient();
        await using OrderFactory orderFactory = new(
            repositoryRoot,
            configureServices: services =>
            {
                services.AddSingleton<IOrderPaymentClient>(new TestOrderPaymentClient(paymentHttpClient));
            });
        using HttpClient orderHttpClient = orderFactory.CreateClient();

        HttpResponseMessage firstResponse = await SendCheckoutAsync(orderHttpClient, "user-0001", "transient_failure", "idem-043-transient");
        HttpResponseMessage secondResponse = await SendCheckoutAsync(orderHttpClient, "user-0001", "transient_failure", "idem-043-transient");

        JsonElement firstBody = JsonSerializer.Deserialize<JsonElement>(await firstResponse.Content.ReadAsStringAsync(), JsonOptions);
        JsonElement secondBody = JsonSerializer.Deserialize<JsonElement>(await secondResponse.Content.ReadAsStringAsync(), JsonOptions);
        string secondTraceId = secondBody.GetProperty("request").GetProperty("traceId").GetString()!;

        Assert.AreEqual("Failed", firstBody.GetProperty("status").GetString());
        Assert.IsTrue(firstBody.GetProperty("contractSatisfied").GetBoolean());
        Assert.AreEqual("Failed", secondBody.GetProperty("status").GetString());
        Assert.IsTrue(secondBody.GetProperty("contractSatisfied").GetBoolean());
        Assert.AreEqual("simulated_transient_failure", firstBody.GetProperty("paymentErrorCode").GetString());
        Assert.AreEqual("simulated_transient_failure", secondBody.GetProperty("paymentErrorCode").GetString());
        Assert.AreEqual(firstBody.GetProperty("orderId").GetString(), secondBody.GetProperty("orderId").GetString());
        Assert.AreEqual(firstBody.GetProperty("paymentId").GetString(), secondBody.GetProperty("paymentId").GetString());
        Assert.AreEqual(firstBody.GetProperty("paymentProviderReference").GetString(), secondBody.GetProperty("paymentProviderReference").GetString());

        await using PrimaryDbContext dbContext = CreateDbContext(repositoryRoot);
        Assert.AreEqual(1, await dbContext.Orders.CountAsync());
        Assert.AreEqual(1, await dbContext.Payments.CountAsync());
        Payment payment = await dbContext.Payments.AsNoTracking().SingleAsync();
        Assert.AreEqual("idem-043-transient", payment.IdempotencyKey);
        Assert.AreEqual("simulated_transient_failure", payment.ErrorCode);

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        Assert.HasCount(1, traces.Where(trace => trace.Record.Service == "PaymentSimulator.Api").ToArray());

        RequestTraceRecord replayTrace = traces
            .Where(trace => trace.Record.Service == "Order.Api")
            .Select(trace => trace.Record)
            .Single(trace => trace.TraceId == secondTraceId);
        Assert.AreEqual("hit", replayTrace.StageTimings.Single(stage => stage.StageName == "idempotency_checked").Outcome);
        Assert.IsEmpty(replayTrace.DependencyCalls);
    }

    [TestMethod]
    public async Task Checkout_DebugTelemetry_ShowsSlowPaymentDominatingMoreThanFastPayment()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 3);
        await SeedActiveCartAsync(repositoryRoot, "user-0001", "sku-0001", 1);
        await SeedActiveCartAsync(repositoryRoot, "user-0002", "sku-0002", 1);

        await using PaymentSimulatorFactory paymentFactory = new(repositoryRoot, CreatePaymentTestOverrides());
        using HttpClient paymentHttpClient = paymentFactory.CreateClient();
        await using OrderFactory orderFactory = new(
            repositoryRoot,
            configureServices: services =>
            {
                services.AddSingleton<IOrderPaymentClient>(new TestOrderPaymentClient(paymentHttpClient));
            });
        using HttpClient orderHttpClient = orderFactory.CreateClient();

        JsonElement fastBody = JsonSerializer.Deserialize<JsonElement>(
            await (await SendCheckoutAsync(orderHttpClient, "user-0001", "fast_success", "idem-044-fast", debugTelemetryRequested: true))
                .Content.ReadAsStringAsync(),
            JsonOptions);
        JsonElement slowBody = JsonSerializer.Deserialize<JsonElement>(
            await (await SendCheckoutAsync(orderHttpClient, "user-0002", "slow_success", "idem-044-slow", debugTelemetryRequested: true))
                .Content.ReadAsStringAsync(),
            JsonOptions);

        JsonElement fastDebug = fastBody.GetProperty("debugTelemetry");
        JsonElement slowDebug = slowBody.GetProperty("debugTelemetry");

        Assert.IsTrue(fastBody.GetProperty("contractSatisfied").GetBoolean());
        Assert.IsTrue(slowBody.GetProperty("contractSatisfied").GetBoolean());

        JsonElement fastDependency = fastDebug.GetProperty("dependencyCalls")[0];
        JsonElement slowDependency = slowDebug.GetProperty("dependencyCalls")[0];
        Assert.AreEqual("payment-simulator", slowDependency.GetProperty("dependencyName").GetString());
        Assert.AreEqual("slow_success", slowDependency.GetProperty("metadata").GetProperty("paymentMode").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(slowDependency.GetProperty("metadata").GetProperty("downstreamTraceId").GetString()));

        string fastPaymentTraceId = fastDependency.GetProperty("metadata").GetProperty("downstreamTraceId").GetString()!;
        string slowPaymentTraceId = slowDependency.GetProperty("metadata").GetProperty("downstreamTraceId").GetString()!;

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceRecord fastPaymentTrace = traces.Single(trace => trace.Record.TraceId == fastPaymentTraceId).Record;
        RequestTraceRecord slowPaymentTrace = traces.Single(trace => trace.Record.TraceId == slowPaymentTraceId).Record;

        double fastAuthorizationElapsed = fastPaymentTrace.StageTimings.Single(stage => stage.StageName == "authorization_simulated").ElapsedMs;
        double slowAuthorizationElapsed = slowPaymentTrace.StageTimings.Single(stage => stage.StageName == "authorization_simulated").ElapsedMs;
        Assert.IsGreaterThan(fastAuthorizationElapsed + 20d, slowAuthorizationElapsed);
    }

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

    private static IReadOnlyDictionary<string, string?> CreatePaymentTestOverrides() =>
        new Dictionary<string, string?>
        {
            ["Lab:PaymentSimulator:DefaultMode"] = "FastSuccess",
            ["Lab:PaymentSimulator:FastLatencyMilliseconds"] = "5",
            ["Lab:PaymentSimulator:SlowLatencyMilliseconds"] = "60",
            ["Lab:PaymentSimulator:TimeoutLatencyMilliseconds"] = "90",
            ["Lab:PaymentSimulator:DelayedConfirmationMilliseconds"] = "75",
            ["Lab:PaymentSimulator:DuplicateCallbackSpacingMilliseconds"] = "25",
            ["Lab:PaymentSimulator:DispatcherPollMilliseconds"] = "10"
        };

    private static async Task<HttpResponseMessage> SendCheckoutAsync(
        HttpClient client,
        string userId,
        string paymentMode,
        string idempotencyKey,
        bool debugTelemetryRequested = false,
        string checkoutMode = "sync")
    {
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            $"/orders/checkout?mode={Uri.EscapeDataString(checkoutMode)}")
        {
            Content = JsonContent.Create(new
            {
                userId,
                paymentMode
            })
        };
        request.Headers.TryAddWithoutValidation(LabHeaderNames.IdempotencyKey, idempotencyKey);

        if (debugTelemetryRequested)
        {
            request.Headers.TryAddWithoutValidation(LabHeaderNames.DebugTelemetry, "true");
        }

        return await client.SendAsync(request);
    }

    private static double FindStageElapsed(JsonElement debugTelemetry, string stageName)
    {
        foreach (JsonElement stage in debugTelemetry.GetProperty("stageTimings").EnumerateArray())
        {
            if (string.Equals(stage.GetProperty("stageName").GetString(), stageName, StringComparison.Ordinal))
            {
                return stage.GetProperty("elapsedMs").GetDouble();
            }
        }

        Assert.Fail($"Stage '{stageName}' was not found in the debug telemetry payload.");
        return 0d;
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
}
