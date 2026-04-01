using System.Text.Json;
using Lab.Persistence;
using Lab.Persistence.Replication;
using Lab.Persistence.Seeding;
using Lab.Shared.Configuration;
using Lab.Shared.Http;
using Lab.Telemetry.RequestTracing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lab.IntegrationTests;

[TestClass]
public sealed class CatalogProductEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task ProductEndpoint_ReturnsProductDetailAndDebugTelemetry_WhenRequested()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);

        await using CatalogFactory factory = new(repositoryRoot);
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage firstRequest = new(HttpMethod.Get, "/catalog/products/sku-0001");
        firstRequest.Headers.TryAddWithoutValidation(LabHeaderNames.DebugTelemetry, "true");
        using HttpRequestMessage secondRequest = new(HttpMethod.Get, "/catalog/products/sku-0001");
        secondRequest.Headers.TryAddWithoutValidation(LabHeaderNames.DebugTelemetry, "true");

        HttpResponseMessage response = await client.SendAsync(firstRequest);
        HttpResponseMessage cachedResponse = await client.SendAsync(secondRequest);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(System.Net.HttpStatusCode.OK, cachedResponse.StatusCode);
        Assert.IsTrue(response.Headers.Contains(LabHeaderNames.RequestId));
        Assert.IsTrue(response.Headers.Contains(LabHeaderNames.TraceId));
        Assert.IsTrue(response.Headers.Contains(LabHeaderNames.RunId));
        Assert.IsTrue(response.Headers.Contains(LabHeaderNames.CorrelationId));

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
        JsonElement cachedBody = JsonSerializer.Deserialize<JsonElement>(await cachedResponse.Content.ReadAsStringAsync(), JsonOptions);

        Assert.AreEqual("sku-0001", body.GetProperty("productId").GetString());
        Assert.AreEqual("primary", body.GetProperty("readSource").GetString());
        Assert.AreEqual("Sample Product 0001", body.GetProperty("name").GetString());
        Assert.AreEqual("apparel", body.GetProperty("category").GetString());
        Assert.AreEqual(1, body.GetProperty("version").GetInt64());
        Assert.AreEqual(1136, body.GetProperty("price").GetProperty("amountCents").GetInt32());
        Assert.AreEqual("USD", body.GetProperty("price").GetProperty("currencyCode").GetString());
        Assert.AreEqual("$11.36", body.GetProperty("price").GetProperty("display").GetString());
        Assert.AreEqual("in_stock", body.GetProperty("inventory").GetProperty("stockStatus").GetString());
        Assert.AreEqual(101, body.GetProperty("inventory").GetProperty("availableQuantity").GetInt32());
        Assert.AreEqual(1, body.GetProperty("inventory").GetProperty("reservedQuantity").GetInt32());
        Assert.AreEqual(100, body.GetProperty("inventory").GetProperty("sellableQuantity").GetInt32());

        JsonElement debugTelemetry = body.GetProperty("debugTelemetry");
        string[] debugStageNames = debugTelemetry
            .GetProperty("stageTimings")
            .EnumerateArray()
            .Select(stage => stage.GetProperty("stageName").GetString()!)
            .ToArray();

        AssertContainsStages(
            debugStageNames,
            "request_received",
            "cache_lookup_started",
            "cache_lookup",
            "cache_lookup_completed",
            "db_query_started",
            "db_query",
            "db_query_completed",
            "freshness_evaluated",
            "response_sent");

        string[] cachedStageNames = cachedBody
            .GetProperty("debugTelemetry")
            .GetProperty("stageTimings")
            .EnumerateArray()
            .Select(stage => stage.GetProperty("stageName").GetString()!)
            .ToArray();

        AssertContainsStages(
            cachedStageNames,
            "request_received",
            "cache_lookup_started",
            "cache_lookup",
            "cache_lookup_completed",
            "freshness_evaluated",
            "response_sent");

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        Assert.HasCount(2, traces);

        RequestTraceTestHelper.TraceEnvelope trace = traces.Single(item => item.Record.TraceId == body.GetProperty("request").GetProperty("traceId").GetString());
        RequestTraceTestHelper.TraceEnvelope cachedTrace = traces.Single(item => item.Record.TraceId == cachedBody.GetProperty("request").GetProperty("traceId").GetString());

        RequestTraceTestHelper.AssertRequiredFieldsPresent(trace.Json);
        RequestTraceTestHelper.AssertRequiredFieldsPresent(cachedTrace.Json);
        Assert.AreEqual("catalog-product-detail", trace.Record.Operation);
        Assert.AreEqual("catalog-product-detail", cachedTrace.Record.Operation);
        Assert.AreEqual("Catalog.Api", trace.Record.Service);
        Assert.AreEqual("Catalog.Api", cachedTrace.Record.Service);
        Assert.AreEqual("/catalog/products/sku-0001", trace.Record.Route);
        Assert.AreEqual("/catalog/products/sku-0001", cachedTrace.Record.Route);
        Assert.AreEqual(200, trace.Record.StatusCode);
        Assert.AreEqual(200, cachedTrace.Record.StatusCode);
        Assert.IsTrue(trace.Record.ContractSatisfied);
        Assert.IsTrue(cachedTrace.Record.ContractSatisfied);
        Assert.IsFalse(trace.Record.CacheHit);
        Assert.IsTrue(cachedTrace.Record.CacheHit);

        CollectionAssert.AreEquivalent(
            new[]
            {
                "request_received",
                "cache_lookup_started",
                "cache_lookup",
                "cache_lookup_completed",
                "db_query_started",
                "db_query",
                "db_query_completed",
                "freshness_evaluated",
                "response_sent",
                "http_request"
            },
            trace.Record.StageTimings.Select(stage => stage.StageName).ToArray());

        CollectionAssert.AreEquivalent(
            new[]
            {
                "request_received",
                "cache_lookup_started",
                "cache_lookup",
                "cache_lookup_completed",
                "freshness_evaluated",
                "response_sent",
                "http_request"
            },
            cachedTrace.Record.StageTimings.Select(stage => stage.StageName).ToArray());

        StageTimingRecord dbQueryCompleted = trace.Record.StageTimings.Single(stage => stage.StageName == "db_query_completed");
        StageTimingRecord cacheLookupCompleted = cachedTrace.Record.StageTimings.Single(stage => stage.StageName == "cache_lookup_completed");
        Assert.AreEqual("true", dbQueryCompleted.Metadata["found"]);
        Assert.AreEqual("in_stock", dbQueryCompleted.Metadata["stockStatus"]);
        Assert.AreEqual("1", dbQueryCompleted.Metadata["version"]);
        Assert.AreEqual("hit", cacheLookupCompleted.Outcome);
        Assert.AreEqual("primary", trace.Record.StageTimings.Single(stage => stage.StageName == "db_query").Metadata["readSource"]);
        Assert.AreEqual("primary", cachedTrace.Record.StageTimings.Single(stage => stage.StageName == "cache_lookup").Metadata["readSource"]);
        CollectionAssert.DoesNotContain(
            cachedTrace.Record.StageTimings.Select(stage => stage.StageName).ToArray(),
            "db_query");
    }

    [TestMethod]
    public async Task ProductEndpoint_NotFoundIsExplicitAndStillSatisfiesTheReadContract()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 2, userCount: 1);

        await using CatalogFactory factory = new(repositoryRoot);
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, "/catalog/products/sku-9999");
        request.Headers.TryAddWithoutValidation(LabHeaderNames.DebugTelemetry, "true");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.AreEqual(System.Net.HttpStatusCode.NotFound, response.StatusCode);

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
        Assert.AreEqual("product_not_found", body.GetProperty("error").GetString());
        Assert.AreEqual("sku-9999", body.GetProperty("productId").GetString());
        Assert.AreEqual("primary", body.GetProperty("readSource").GetString());
        Assert.IsTrue(body.TryGetProperty("debugTelemetry", out JsonElement debugTelemetry));
        CollectionAssert.Contains(
            debugTelemetry.GetProperty("stageTimings")
                .EnumerateArray()
                .Select(stage => stage.GetProperty("stageName").GetString()!)
                .ToArray(),
            "db_query_completed");

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        Assert.HasCount(1, traces);

        RequestTraceRecord record = traces[0].Record;
        Assert.AreEqual("catalog-product-detail", record.Operation);
        Assert.AreEqual("Catalog.Api", record.Service);
        Assert.AreEqual("/catalog/products/sku-9999", record.Route);
        Assert.AreEqual(404, record.StatusCode);
        Assert.IsTrue(record.ContractSatisfied);
        Assert.IsFalse(record.CacheHit);
        Assert.AreEqual("product_not_found", record.ErrorCode);
        Assert.AreEqual("miss", record.StageTimings.Single(stage => stage.StageName == "cache_lookup_completed").Outcome);
        Assert.AreEqual("not_found", record.StageTimings.Single(stage => stage.StageName == "db_query_completed").Outcome);
        Assert.AreEqual("false", record.StageTimings.Single(stage => stage.StageName == "db_query_completed").Metadata["found"]);
        Assert.AreEqual("primary", record.StageTimings.Single(stage => stage.StageName == "db_query_completed").Metadata["readSource"]);
        Assert.AreEqual("not_found", record.StageTimings.Single(stage => stage.StageName == "response_sent").Outcome);
        Assert.AreEqual("error", record.StageTimings.Single(stage => stage.StageName == "http_request").Outcome);
    }

    [TestMethod]
    public async Task ProductEndpoint_ReadSourceCanSwitchBetweenPrimaryAndReplicaWithoutMixingCacheEntries()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);
        await SyncReplicasAsync(repositoryRoot);
        await MutatePrimaryProductAsync(repositoryRoot, "sku-0001", productVersion: 44, inventoryVersion: 52, availableQuantity: 7, reservedQuantity: 0);

        await using CatalogFactory factory = new(repositoryRoot);
        using HttpClient client = factory.CreateClient();

        using HttpRequestMessage replicaRequest = new(HttpMethod.Get, "/catalog/products/sku-0001?readSource=replica-east");
        replicaRequest.Headers.TryAddWithoutValidation(LabHeaderNames.DebugTelemetry, "true");
        using HttpRequestMessage primaryRequest = new(HttpMethod.Get, "/catalog/products/sku-0001?readSource=primary");
        primaryRequest.Headers.TryAddWithoutValidation(LabHeaderNames.DebugTelemetry, "true");

        HttpResponseMessage replicaResponse = await client.SendAsync(replicaRequest);
        HttpResponseMessage primaryResponse = await client.SendAsync(primaryRequest);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, replicaResponse.StatusCode);
        Assert.AreEqual(System.Net.HttpStatusCode.OK, primaryResponse.StatusCode);

        JsonElement replicaBody = JsonSerializer.Deserialize<JsonElement>(await replicaResponse.Content.ReadAsStringAsync(), JsonOptions);
        JsonElement primaryBody = JsonSerializer.Deserialize<JsonElement>(await primaryResponse.Content.ReadAsStringAsync(), JsonOptions);

        Assert.AreEqual("replica-east", replicaBody.GetProperty("readSource").GetString());
        Assert.AreEqual("primary", primaryBody.GetProperty("readSource").GetString());
        Assert.AreEqual(1, replicaBody.GetProperty("version").GetInt64());
        Assert.AreEqual(44, primaryBody.GetProperty("version").GetInt64());
        Assert.AreEqual(100, replicaBody.GetProperty("inventory").GetProperty("sellableQuantity").GetInt32());
        Assert.AreEqual(7, primaryBody.GetProperty("inventory").GetProperty("sellableQuantity").GetInt32());

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceRecord replicaTrace = traces.Single(trace => trace.Record.TraceId == replicaBody.GetProperty("request").GetProperty("traceId").GetString()).Record;
        RequestTraceRecord primaryTrace = traces.Single(trace => trace.Record.TraceId == primaryBody.GetProperty("request").GetProperty("traceId").GetString()).Record;

        Assert.IsFalse(replicaTrace.CacheHit);
        Assert.IsFalse(primaryTrace.CacheHit);
        Assert.AreEqual("replica-east", replicaTrace.StageTimings.Single(stage => stage.StageName == "db_query").Metadata["readSource"]);
        Assert.AreEqual("replica-east.db", replicaTrace.StageTimings.Single(stage => stage.StageName == "db_query").Metadata["source"]);
        Assert.AreEqual("primary", primaryTrace.StageTimings.Single(stage => stage.StageName == "db_query").Metadata["readSource"]);
        Assert.AreEqual("primary.db", primaryTrace.StageTimings.Single(stage => stage.StageName == "db_query").Metadata["source"]);
    }

    [TestMethod]
    public async Task ProductEndpoint_LocalReadSource_UsesSameRegionReplicaWhenAvailable()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);
        await SyncReplicasAsync(repositoryRoot);
        await MutatePrimaryProductAsync(repositoryRoot, "sku-0001", productVersion: 41, inventoryVersion: 52, availableQuantity: 9, reservedQuantity: 0);

        await using CatalogFactory factory = new(
            repositoryRoot,
            new Dictionary<string, string?>
            {
                [$"{LabConfigurationSections.Regions}:CurrentRegion"] = "us-west",
                [$"{LabConfigurationSections.Regions}:PrimaryRegion"] = "us-east",
                [$"{LabConfigurationSections.Regions}:WestReplicaRegion"] = "us-west"
            });
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, "/catalog/products/sku-0001?readSource=local");
        request.Headers.TryAddWithoutValidation(LabHeaderNames.DebugTelemetry, "true");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode);

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
        Assert.AreEqual("replica-west", body.GetProperty("readSource").GetString());
        Assert.AreEqual(1, body.GetProperty("version").GetInt64());
        Assert.IsTrue(body.GetProperty("freshness").GetProperty("staleRead").GetBoolean());

        RequestTraceRecord trace = (await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot))
            .Single(item => item.Record.TraceId == body.GetProperty("request").GetProperty("traceId").GetString())
            .Record;

        StageTimingRecord dbQuery = trace.StageTimings.Single(stage => stage.StageName == "db_query");
        Assert.AreEqual("local", dbQuery.Metadata["requestedReadSource"]);
        Assert.AreEqual("replica-west", dbQuery.Metadata["effectiveReadSource"]);
        Assert.AreEqual("same-region", dbQuery.Metadata["selectionScope"]);
        Assert.AreEqual("us-west", dbQuery.Metadata["targetRegion"]);
        Assert.AreEqual("false", dbQuery.Metadata["fallbackApplied"]);
        Assert.AreEqual("replica-west", trace.ReadSource);
    }

    [TestMethod]
    public async Task ProductEndpoint_LocalReadSource_FallsBackToPrimaryWhenNoLocalReplicaMatchesRegion()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);
        await MutatePrimaryProductAsync(repositoryRoot, "sku-0001", productVersion: 44, inventoryVersion: 53, availableQuantity: 8, reservedQuantity: 0);

        await using CatalogFactory factory = new(
            repositoryRoot,
            new Dictionary<string, string?>
            {
                [$"{LabConfigurationSections.Regions}:CurrentRegion"] = "eu-central",
                [$"{LabConfigurationSections.Regions}:PrimaryRegion"] = "us-east",
                [$"{LabConfigurationSections.Regions}:EastReplicaRegion"] = "us-east",
                [$"{LabConfigurationSections.Regions}:WestReplicaRegion"] = "us-west"
            });
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, "/catalog/products/sku-0001?readSource=local");
        request.Headers.TryAddWithoutValidation(LabHeaderNames.DebugTelemetry, "true");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode);

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
        Assert.AreEqual("primary", body.GetProperty("readSource").GetString());
        Assert.AreEqual(44, body.GetProperty("version").GetInt64());
        Assert.IsFalse(body.GetProperty("freshness").GetProperty("staleRead").GetBoolean());

        RequestTraceRecord trace = (await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot))
            .Single(item => item.Record.TraceId == body.GetProperty("request").GetProperty("traceId").GetString())
            .Record;

        StageTimingRecord dbQuery = trace.StageTimings.Single(stage => stage.StageName == "db_query");
        Assert.AreEqual("local", dbQuery.Metadata["requestedReadSource"]);
        Assert.AreEqual("primary", dbQuery.Metadata["effectiveReadSource"]);
        Assert.AreEqual("cross-region", dbQuery.Metadata["selectionScope"]);
        Assert.AreEqual("us-east", dbQuery.Metadata["targetRegion"]);
        Assert.AreEqual("true", dbQuery.Metadata["fallbackApplied"]);
        Assert.AreEqual("no_local_product_read_source_for_region", dbQuery.Metadata["fallbackReason"]);
        Assert.AreEqual("primary", trace.ReadSource);
    }

    [TestMethod]
    public async Task ProductEndpoint_LocalReadSource_FallsBackToPrimaryWhenLocalReplicaIsSimulatedUnavailable()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);
        await SyncReplicasAsync(repositoryRoot);
        await MutatePrimaryProductAsync(repositoryRoot, "sku-0001", productVersion: 46, inventoryVersion: 58, availableQuantity: 6, reservedQuantity: 0);

        await using CatalogFactory factory = new(
            repositoryRoot,
            new Dictionary<string, string?>
            {
                [$"{LabConfigurationSections.Regions}:CurrentRegion"] = "us-west",
                [$"{LabConfigurationSections.Regions}:PrimaryRegion"] = "us-east",
                [$"{LabConfigurationSections.Regions}:WestReplicaRegion"] = "us-west",
                [$"{LabConfigurationSections.RegionalDegradation}:SimulateLocalReplicaUnavailable"] = "true"
            });
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/catalog/products/sku-0001?readSource=local");

        Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode);

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
        Assert.AreEqual("primary", body.GetProperty("readSource").GetString());
        Assert.AreEqual(46, body.GetProperty("version").GetInt64());

        RequestTraceRecord trace = (await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot))
            .Single(item => item.Record.TraceId == body.GetProperty("request").GetProperty("traceId").GetString())
            .Record;

        StageTimingRecord dbQuery = trace.StageTimings.Single(stage => stage.StageName == "db_query");
        Assert.AreEqual("local", dbQuery.Metadata["requestedReadSource"]);
        Assert.AreEqual("primary", dbQuery.Metadata["effectiveReadSource"]);
        Assert.AreEqual("cross-region", dbQuery.Metadata["selectionScope"]);
        Assert.AreEqual("us-east", dbQuery.Metadata["targetRegion"]);
        Assert.AreEqual("true", dbQuery.Metadata["fallbackApplied"]);
        Assert.AreEqual("local_replica_unavailable", dbQuery.Metadata["fallbackReason"]);
        Assert.AreEqual("cross-region", dbQuery.Metadata["readNetworkScope"]);
        Assert.AreEqual("primary", trace.ReadSource);
        Assert.IsTrue(trace.Notes.Any(note => note.Contains("local replica was marked unavailable", StringComparison.Ordinal)));
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

    private static async Task SyncReplicasAsync(string repositoryRoot)
    {
        PrimaryDbContextFactory dbContextFactory = new();
        PrimaryDatabaseInitializer initializer = new(dbContextFactory);
        ReplicaSyncService replicaSyncService = new(
            dbContextFactory,
            initializer,
            TimeProvider.System,
            NullLogger<ReplicaSyncService>.Instance);

        await replicaSyncService.SynchronizeAsync(
            Path.Combine(repositoryRoot, "data", "primary.db"),
            [
                new ReplicaSyncTarget("replica-east", Path.Combine(repositoryRoot, "data", "replica-east.db"), TimeSpan.Zero),
                new ReplicaSyncTarget("replica-west", Path.Combine(repositoryRoot, "data", "replica-west.db"), TimeSpan.Zero)
            ]);
    }

    private static async Task MutatePrimaryProductAsync(
        string repositoryRoot,
        string productId,
        long productVersion,
        long inventoryVersion,
        int availableQuantity,
        int reservedQuantity)
    {
        await using PrimaryDbContext dbContext = CreateDbContext(repositoryRoot);
        Lab.Persistence.Entities.Product product = await dbContext.Products.SingleAsync(item => item.ProductId == productId);
        Lab.Persistence.Entities.InventoryRecord inventory = await dbContext.Inventory.SingleAsync(item => item.ProductId == productId);
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

        product.Version = productVersion;
        product.UpdatedUtc = nowUtc;
        inventory.AvailableQuantity = availableQuantity;
        inventory.ReservedQuantity = reservedQuantity;
        inventory.Version = inventoryVersion;
        inventory.UpdatedUtc = nowUtc;

        await dbContext.SaveChangesAsync();
    }

    private static PrimaryDbContext CreateDbContext(string repositoryRoot)
    {
        PrimaryDbContextFactory dbContextFactory = new();
        return dbContextFactory.CreateDbContext(Path.Combine(repositoryRoot, "data", "primary.db"));
    }

    private static string CreateUniqueTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AssertContainsStages(IEnumerable<string> actualStageNames, params string[] expectedStageNames)
    {
        string[] actual = actualStageNames.ToArray();

        foreach (string expectedStageName in expectedStageNames)
        {
            CollectionAssert.Contains(actual, expectedStageName);
        }
    }
}
