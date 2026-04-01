using System.Net.Http.Json;
using System.Text.Json;
using Lab.Persistence;
using Lab.Persistence.Entities;
using Lab.Persistence.Seeding;
using Lab.Telemetry.RequestTracing;
using Microsoft.EntityFrameworkCore;
using CartEntity = Lab.Persistence.Entities.Cart;
using CartItemEntity = Lab.Persistence.Entities.CartItem;

namespace Lab.IntegrationTests;

[TestClass]
public sealed class CartEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task AddItemEndpoint_CreatesCart_AccumulatesQuantity_AndStoresPriceSnapshot()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);

        await using CartFactory factory = new(repositoryRoot);
        using HttpClient client = factory.CreateClient();

        JsonElement firstResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/cart/items",
            new
            {
                userId = "user-0001",
                productId = "sku-0001",
                quantity = 2
            });

        JsonElement secondResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/cart/items",
            new
            {
                userId = "user-0001",
                productId = "sku-0001",
                quantity = 3
            });

        string firstTraceId = firstResponse.GetProperty("request").GetProperty("traceId").GetString()!;
        string secondTraceId = secondResponse.GetProperty("request").GetProperty("traceId").GetString()!;

        Assert.IsTrue(firstResponse.GetProperty("exists").GetBoolean());
        Assert.AreEqual("created", firstResponse.GetProperty("loadOutcome").GetString());
        Assert.AreEqual("added", firstResponse.GetProperty("mutationOutcome").GetString());
        Assert.AreEqual("loaded", secondResponse.GetProperty("loadOutcome").GetString());
        Assert.AreEqual("accumulated", secondResponse.GetProperty("mutationOutcome").GetString());
        Assert.AreEqual("user-0001", secondResponse.GetProperty("userId").GetString());
        Assert.AreEqual(1, secondResponse.GetProperty("distinctItemCount").GetInt32());
        Assert.AreEqual(5, secondResponse.GetProperty("totalQuantity").GetInt32());
        Assert.AreEqual(5680, secondResponse.GetProperty("totalPriceCents").GetInt32());

        JsonElement line = secondResponse.GetProperty("items").EnumerateArray().Single();
        Assert.AreEqual("sku-0001", line.GetProperty("productId").GetString());
        Assert.AreEqual(5, line.GetProperty("quantity").GetInt32());
        Assert.AreEqual(1136, line.GetProperty("unitPriceSnapshotCents").GetInt32());
        Assert.AreEqual(5680, line.GetProperty("lineSubtotalCents").GetInt32());

        await using PrimaryDbContext dbContext = CreateDbContext(repositoryRoot);
        CartEntity cart = await dbContext.Carts
            .Include(existingCart => existingCart.Items)
            .SingleAsync(existingCart => existingCart.UserId == "user-0001" && existingCart.Status == "active");

        Assert.AreEqual("local", cart.Region);
        Assert.HasCount(1, cart.Items);
        CartItemEntity item = cart.Items.Single();
        Assert.AreEqual("sku-0001", item.ProductId);
        Assert.AreEqual(5, item.Quantity);
        Assert.AreEqual(1136, item.UnitPriceCents);

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        Assert.HasCount(2, traces);

        RequestTraceRecord firstTrace = traces.Single(trace => trace.Record.TraceId == firstTraceId).Record;
        RequestTraceRecord secondTrace = traces.Single(trace => trace.Record.TraceId == secondTraceId).Record;

        Assert.AreEqual("cart-add-item", firstTrace.Operation);
        Assert.AreEqual("cart-add-item", secondTrace.Operation);
        Assert.AreEqual("Cart.Api", firstTrace.Service);
        Assert.AreEqual("Cart.Api", secondTrace.Service);
        Assert.AreEqual("/cart/items", firstTrace.Route);
        Assert.AreEqual("/cart/items", secondTrace.Route);
        Assert.AreEqual("user-0001", firstTrace.UserId);
        Assert.AreEqual("user-0001", secondTrace.UserId);
        Assert.AreEqual("created", firstTrace.StageTimings.Single(stage => stage.StageName == "cart_loaded_or_created").Outcome);
        Assert.AreEqual("added", firstTrace.StageTimings.Single(stage => stage.StageName == "cart_mutated").Outcome);
        Assert.AreEqual("success", firstTrace.StageTimings.Single(stage => stage.StageName == "cart_persisted").Outcome);
        Assert.AreEqual("loaded", secondTrace.StageTimings.Single(stage => stage.StageName == "cart_loaded_or_created").Outcome);
        Assert.AreEqual("accumulated", secondTrace.StageTimings.Single(stage => stage.StageName == "cart_mutated").Outcome);
        Assert.AreEqual("success", secondTrace.StageTimings.Single(stage => stage.StageName == "cart_persisted").Outcome);

        CollectionAssert.AreEquivalent(
            new[]
            {
                "request_received",
                "cart_loaded_or_created",
                "cart_mutated",
                "cart_persisted",
                "response_sent",
                "http_request"
            },
            secondTrace.StageTimings.Select(stage => stage.StageName).ToArray());
    }

    [TestMethod]
    public async Task DeleteAndGetCartEndpoints_DecrementRemoveAndListPersistedCartState()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);

        await using CartFactory factory = new(repositoryRoot);
        using HttpClient client = factory.CreateClient();

        _ = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/cart/items",
            new
            {
                userId = "user-0001",
                productId = "sku-0002",
                quantity = 4
            });

        JsonElement decrementResponse = await SendJsonAsync(
            client,
            HttpMethod.Delete,
            "/cart/items",
            new
            {
                userId = "user-0001",
                productId = "sku-0002",
                quantity = 1
            });

        JsonElement removeResponse = await SendJsonAsync(
            client,
            HttpMethod.Delete,
            "/cart/items",
            new
            {
                userId = "user-0001",
                productId = "sku-0002",
                quantity = 3
            });

        HttpResponseMessage getResponse = await client.GetAsync("/cart/user-0001");
        Assert.AreEqual(System.Net.HttpStatusCode.OK, getResponse.StatusCode);
        JsonElement cartResponse = JsonSerializer.Deserialize<JsonElement>(await getResponse.Content.ReadAsStringAsync(), JsonOptions);

        Assert.AreEqual("decremented", decrementResponse.GetProperty("mutationOutcome").GetString());
        Assert.AreEqual(3, decrementResponse.GetProperty("items").EnumerateArray().Single().GetProperty("quantity").GetInt32());
        Assert.AreEqual("removed", removeResponse.GetProperty("mutationOutcome").GetString());
        Assert.IsTrue(removeResponse.GetProperty("exists").GetBoolean());
        Assert.AreEqual(0, removeResponse.GetProperty("distinctItemCount").GetInt32());
        Assert.AreEqual(0, removeResponse.GetProperty("totalQuantity").GetInt32());
        Assert.AreEqual(0, removeResponse.GetProperty("totalPriceCents").GetInt32());
        Assert.IsEmpty(removeResponse.GetProperty("items").EnumerateArray().ToArray());

        Assert.IsTrue(cartResponse.GetProperty("exists").GetBoolean());
        Assert.AreEqual("active", cartResponse.GetProperty("status").GetString());
        Assert.AreEqual("loaded", cartResponse.GetProperty("loadOutcome").GetString());
        Assert.AreEqual("read_only", cartResponse.GetProperty("mutationOutcome").GetString());
        Assert.IsEmpty(cartResponse.GetProperty("items").EnumerateArray().ToArray());

        await using PrimaryDbContext dbContext = CreateDbContext(repositoryRoot);
        CartEntity cart = await dbContext.Carts
            .Include(existingCart => existingCart.Items)
            .SingleAsync(existingCart => existingCart.UserId == "user-0001" && existingCart.Status == "active");

        Assert.HasCount(0, cart.Items);

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        Assert.HasCount(4, traces);

        RequestTraceRecord[] deleteTraces = traces
            .Select(trace => trace.Record)
            .Where(record => record.Operation == "cart-remove-item")
            .ToArray();
        RequestTraceRecord getTrace = traces
            .Select(trace => trace.Record)
            .Single(record => record.Operation == "cart-get");

        Assert.HasCount(2, deleteTraces);
        CollectionAssert.AreEquivalent(
            new[] { "decremented", "removed" },
            deleteTraces.Select(record => record.StageTimings.Single(stage => stage.StageName == "cart_mutated").Outcome).ToArray());
        Assert.AreEqual("not_required", getTrace.StageTimings.Single(stage => stage.StageName == "cart_persisted").Outcome);
        Assert.AreEqual("read_only", getTrace.StageTimings.Single(stage => stage.StageName == "cart_mutated").Outcome);
        CollectionAssert.AreEquivalent(
            new[]
            {
                "request_received",
                "cart_loaded_or_created",
                "cart_mutated",
                "cart_persisted",
                "response_sent",
                "http_request"
            },
            getTrace.StageTimings.Select(stage => stage.StageName).ToArray());
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

    private static PrimaryDbContext CreateDbContext(string repositoryRoot)
    {
        PrimaryDbContextFactory dbContextFactory = new();
        string databasePath = Path.Combine(repositoryRoot, "data", "primary.db");
        return dbContextFactory.CreateDbContext(databasePath);
    }

    private static async Task<JsonElement> SendJsonAsync(HttpClient client, HttpMethod method, string path, object payload)
    {
        using HttpRequestMessage request = new(method, path)
        {
            Content = JsonContent.Create(payload)
        };

        HttpResponseMessage response = await client.SendAsync(request);
        Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode);
        return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
    }

    private static string CreateUniqueTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
