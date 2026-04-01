using System.Text.Json;
using Lab.Persistence;
using Lab.Persistence.Replication;
using Lab.Persistence.Seeding;
using Lab.Shared.Configuration;
using Lab.Shared.Http;
using Lab.Telemetry.RequestTracing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Storefront.Api.ProductPages;

namespace Lab.IntegrationTests;

[TestClass]
public sealed class StorefrontProductEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task ProductEndpoint_CacheOn_MissThenHit_ChangesThePathShapeAndRecordsCacheHit()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);

        await using CatalogFactory catalogFactory = new(repositoryRoot);
        using HttpClient catalogClient = catalogFactory.CreateClient();
        await using StorefrontFactory storefrontFactory = new(
            repositoryRoot,
            configureServices: services =>
            {
                services.AddSingleton<ICatalogProductClient>(new TestCatalogProductClient(catalogClient));
            });
        using HttpClient storefrontClient = storefrontFactory.CreateClient();

        using HttpRequestMessage firstRequest = new(HttpMethod.Get, "/products/sku-0001?cache=on");
        firstRequest.Headers.TryAddWithoutValidation(LabHeaderNames.DebugTelemetry, "true");
        using HttpRequestMessage secondRequest = new(HttpMethod.Get, "/products/sku-0001?cache=on");
        secondRequest.Headers.TryAddWithoutValidation(LabHeaderNames.DebugTelemetry, "true");

        HttpResponseMessage firstResponse = await storefrontClient.SendAsync(firstRequest);
        HttpResponseMessage secondResponse = await storefrontClient.SendAsync(secondRequest);

        Assert.AreEqual(System.Net.HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.AreEqual(System.Net.HttpStatusCode.OK, secondResponse.StatusCode);

        JsonElement firstJson = JsonSerializer.Deserialize<JsonElement>(await firstResponse.Content.ReadAsStringAsync(), JsonOptions);
        JsonElement secondJson = JsonSerializer.Deserialize<JsonElement>(await secondResponse.Content.ReadAsStringAsync(), JsonOptions);
        string firstStorefrontTraceId = firstJson.GetProperty("request").GetProperty("traceId").GetString()!;
        string secondStorefrontTraceId = secondJson.GetProperty("request").GetProperty("traceId").GetString()!;

        Assert.AreEqual("sku-0001", firstJson.GetProperty("productId").GetString());
        Assert.AreEqual("catalog-api", firstJson.GetProperty("source").GetString());
        Assert.AreEqual("primary", firstJson.GetProperty("readSource").GetString());
        Assert.AreEqual("on", firstJson.GetProperty("cache").GetProperty("mode").GetString());
        Assert.IsFalse(firstJson.GetProperty("cache").GetProperty("hit").GetBoolean());
        Assert.AreEqual("storefront-cache", secondJson.GetProperty("source").GetString());
        Assert.AreEqual("primary", secondJson.GetProperty("readSource").GetString());
        Assert.IsTrue(secondJson.GetProperty("cache").GetProperty("hit").GetBoolean());
        Assert.AreEqual("Catalog.Api", firstJson.GetProperty("catalog").GetProperty("service").GetString());
        Assert.AreEqual("Catalog.Api", secondJson.GetProperty("catalog").GetProperty("service").GetString());

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceTestHelper.TraceEnvelope[] storefrontTraces = traces.Where(trace => trace.Record.Service == "Storefront.Api").ToArray();
        RequestTraceTestHelper.TraceEnvelope[] catalogTraces = traces.Where(trace => trace.Record.Service == "Catalog.Api").ToArray();

        Assert.HasCount(2, storefrontTraces);
        Assert.HasCount(1, catalogTraces);

        RequestTraceTestHelper.TraceEnvelope firstStorefrontTrace = storefrontTraces.Single(trace => trace.Record.TraceId == firstStorefrontTraceId);
        RequestTraceTestHelper.TraceEnvelope secondStorefrontTrace = storefrontTraces.Single(trace => trace.Record.TraceId == secondStorefrontTraceId);

        RequestTraceTestHelper.AssertRequiredFieldsPresent(firstStorefrontTrace.Json);
        RequestTraceTestHelper.AssertRequiredFieldsPresent(secondStorefrontTrace.Json);
        Assert.AreEqual("product-page", firstStorefrontTrace.Record.Operation);
        Assert.AreEqual("product-page", secondStorefrontTrace.Record.Operation);
        Assert.AreEqual("/products/sku-0001", firstStorefrontTrace.Record.Route);
        Assert.AreEqual("/products/sku-0001", secondStorefrontTrace.Record.Route);
        Assert.IsTrue(firstStorefrontTrace.Record.ContractSatisfied);
        Assert.IsTrue(secondStorefrontTrace.Record.ContractSatisfied);
        Assert.IsFalse(firstStorefrontTrace.Record.CacheHit);
        Assert.IsTrue(secondStorefrontTrace.Record.CacheHit);
        Assert.HasCount(1, firstStorefrontTrace.Record.DependencyCalls);
        Assert.IsEmpty(secondStorefrontTrace.Record.DependencyCalls);
        Assert.AreEqual(firstStorefrontTrace.Record.CorrelationId, catalogTraces[0].Record.CorrelationId);

        CollectionAssert.AreEquivalent(
            new[]
            {
                "request_received",
                "cache_lookup",
                "catalog_call_started",
                "catalog_call_completed",
                "freshness_evaluated",
                "response_sent",
                "http_request"
            },
            firstStorefrontTrace.Record.StageTimings.Select(stage => stage.StageName).ToArray());

        CollectionAssert.AreEquivalent(
            new[]
            {
                "request_received",
                "cache_lookup",
                "freshness_evaluated",
                "response_sent",
                "http_request"
            },
            secondStorefrontTrace.Record.StageTimings.Select(stage => stage.StageName).ToArray());

        Assert.AreEqual("hit", secondStorefrontTrace.Record.StageTimings.Single(stage => stage.StageName == "cache_lookup").Outcome);
        CollectionAssert.DoesNotContain(
            secondStorefrontTrace.Record.StageTimings.Select(stage => stage.StageName).ToArray(),
            "catalog_call_started");
        Assert.AreEqual("catalog-api", firstStorefrontTrace.Record.DependencyCalls[0].DependencyName);
        Assert.AreEqual(200, firstStorefrontTrace.Record.DependencyCalls[0].StatusCode);
    }

    [TestMethod]
    public async Task ProductEndpoint_CacheOff_CallsCatalogEveryTime()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);

        await using CatalogFactory catalogFactory = new(repositoryRoot);
        using HttpClient catalogClient = catalogFactory.CreateClient();
        await using StorefrontFactory storefrontFactory = new(
            repositoryRoot,
            configureServices: services =>
            {
                services.AddSingleton<ICatalogProductClient>(new TestCatalogProductClient(catalogClient));
            });
        using HttpClient storefrontClient = storefrontFactory.CreateClient();

        HttpResponseMessage firstResponse = await storefrontClient.GetAsync("/products/sku-0002?cache=off");
        HttpResponseMessage secondResponse = await storefrontClient.GetAsync("/products/sku-0002?cache=off");

        Assert.AreEqual(System.Net.HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.AreEqual(System.Net.HttpStatusCode.OK, secondResponse.StatusCode);

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceTestHelper.TraceEnvelope[] storefrontTraces = traces.Where(trace => trace.Record.Service == "Storefront.Api").ToArray();
        RequestTraceTestHelper.TraceEnvelope[] catalogTraces = traces.Where(trace => trace.Record.Service == "Catalog.Api").ToArray();

        Assert.HasCount(2, storefrontTraces);
        Assert.HasCount(2, catalogTraces);

        foreach (RequestTraceTestHelper.TraceEnvelope storefrontTrace in storefrontTraces)
        {
            Assert.IsFalse(storefrontTrace.Record.CacheHit);
            Assert.AreEqual("bypassed", storefrontTrace.Record.StageTimings.Single(stage => stage.StageName == "cache_lookup").Outcome);
            Assert.AreEqual("primary", storefrontTrace.Record.StageTimings.Single(stage => stage.StageName == "cache_lookup").Metadata["readSource"]);
            CollectionAssert.Contains(
                storefrontTrace.Record.StageTimings.Select(stage => stage.StageName).ToArray(),
                "catalog_call_started");
            CollectionAssert.Contains(
                storefrontTrace.Record.StageTimings.Select(stage => stage.StageName).ToArray(),
                "catalog_call_completed");
            Assert.HasCount(1, storefrontTrace.Record.DependencyCalls);
            Assert.AreEqual(200, storefrontTrace.Record.DependencyCalls[0].StatusCode);
        }
    }

    [TestMethod]
    public async Task ProductEndpoint_ReadSourceSelectionFlowsThroughStorefrontAndPreservesReplicaLag()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);
        await SyncReplicasAsync(repositoryRoot);
        await MutatePrimaryProductAsync(repositoryRoot, "sku-0001", productVersion: 45, inventoryVersion: 53, availableQuantity: 8, reservedQuantity: 0);

        await using CatalogFactory catalogFactory = new(repositoryRoot);
        using HttpClient catalogClient = catalogFactory.CreateClient();
        await using StorefrontFactory storefrontFactory = new(
            repositoryRoot,
            configureServices: services =>
            {
                services.AddSingleton<ICatalogProductClient>(new TestCatalogProductClient(catalogClient));
            });
        using HttpClient storefrontClient = storefrontFactory.CreateClient();

        HttpResponseMessage replicaResponse = await storefrontClient.GetAsync("/products/sku-0001?cache=off&readSource=replica-east");
        HttpResponseMessage primaryResponse = await storefrontClient.GetAsync("/products/sku-0001?cache=off&readSource=primary");

        Assert.AreEqual(System.Net.HttpStatusCode.OK, replicaResponse.StatusCode);
        Assert.AreEqual(System.Net.HttpStatusCode.OK, primaryResponse.StatusCode);

        JsonElement replicaBody = JsonSerializer.Deserialize<JsonElement>(await replicaResponse.Content.ReadAsStringAsync(), JsonOptions);
        JsonElement primaryBody = JsonSerializer.Deserialize<JsonElement>(await primaryResponse.Content.ReadAsStringAsync(), JsonOptions);

        Assert.AreEqual("catalog-api", replicaBody.GetProperty("source").GetString());
        Assert.AreEqual("replica-east", replicaBody.GetProperty("readSource").GetString());
        Assert.AreEqual(1, replicaBody.GetProperty("version").GetInt64());
        Assert.AreEqual(100, replicaBody.GetProperty("inventory").GetProperty("sellableQuantity").GetInt32());
        Assert.IsTrue(replicaBody.GetProperty("freshness").GetProperty("staleRead").GetBoolean());
        Assert.AreEqual("replica-east", replicaBody.GetProperty("freshness").GetProperty("readSource").GetString());
        Assert.AreEqual(1, replicaBody.GetProperty("freshness").GetProperty("comparedCount").GetInt32());
        Assert.AreEqual(1, replicaBody.GetProperty("freshness").GetProperty("staleCount").GetInt32());
        Assert.IsGreaterThan(0d, replicaBody.GetProperty("freshness").GetProperty("maxStalenessAgeMs").GetDouble());
        Assert.AreEqual("primary", primaryBody.GetProperty("readSource").GetString());
        Assert.AreEqual(45, primaryBody.GetProperty("version").GetInt64());
        Assert.AreEqual(8, primaryBody.GetProperty("inventory").GetProperty("sellableQuantity").GetInt32());
        Assert.IsFalse(primaryBody.GetProperty("freshness").GetProperty("staleRead").GetBoolean());

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceRecord replicaTrace = traces.Single(trace => trace.Record.TraceId == replicaBody.GetProperty("request").GetProperty("traceId").GetString()).Record;
        RequestTraceRecord primaryTrace = traces.Single(trace => trace.Record.TraceId == primaryBody.GetProperty("request").GetProperty("traceId").GetString()).Record;

        Assert.AreEqual("replica-east", replicaTrace.StageTimings.Single(stage => stage.StageName == "catalog_call_completed").Metadata["readSource"]);
        Assert.AreEqual("primary", primaryTrace.StageTimings.Single(stage => stage.StageName == "catalog_call_completed").Metadata["readSource"]);
        Assert.AreEqual("replica-east", replicaTrace.DependencyCalls[0].Metadata["readSource"]);
        Assert.AreEqual("primary", primaryTrace.DependencyCalls[0].Metadata["readSource"]);
        Assert.AreEqual("replica-east", replicaTrace.ReadSource);
        Assert.AreEqual(1, replicaTrace.FreshnessComparedCount);
        Assert.AreEqual(1, replicaTrace.FreshnessStaleCount);
        Assert.AreEqual(1d, replicaTrace.FreshnessStaleFraction!.Value, 0.0001d);
        Assert.IsGreaterThan(0d, replicaTrace.MaxStalenessAgeMs!.Value);
        Assert.AreEqual("primary", primaryTrace.ReadSource);
        Assert.AreEqual(1, primaryTrace.FreshnessComparedCount);
        Assert.AreEqual(0, primaryTrace.FreshnessStaleCount);
    }

    [TestMethod]
    public async Task ProductEndpoint_DependencyTrace_RecordsConfiguredRegionEnvelope()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);

        await using CatalogFactory catalogFactory = new(
            repositoryRoot,
            new Dictionary<string, string?>
            {
                [$"{LabConfigurationSections.Regions}:CurrentRegion"] = "us-west"
            });
        using HttpClient catalogClient = catalogFactory.CreateClient();

        await using StorefrontFactory storefrontFactory = new(
            repositoryRoot,
            configureServices: services =>
            {
                services.AddSingleton<ICatalogProductClient>(new TestCatalogProductClient(catalogClient));
            },
            configurationOverrides: new Dictionary<string, string?>
            {
                [$"{LabConfigurationSections.Regions}:CurrentRegion"] = "us-east",
                [$"{LabConfigurationSections.Regions}:SameRegionLatencyMs"] = "2",
                [$"{LabConfigurationSections.Regions}:CrossRegionLatencyMs"] = "17",
                [$"{LabConfigurationSections.ServiceEndpoints}:CatalogRegion"] = "us-west"
            });
        using HttpClient storefrontClient = storefrontFactory.CreateClient();

        HttpResponseMessage response = await storefrontClient.GetAsync("/products/sku-0001?cache=off&readSource=primary");

        Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode);

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
        RequestTraceRecord storefrontTrace = (await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot))
            .Single(trace => trace.Record.TraceId == body.GetProperty("request").GetProperty("traceId").GetString())
            .Record;

        Assert.HasCount(1, storefrontTrace.DependencyCalls);
        DependencyCallRecord dependency = storefrontTrace.DependencyCalls[0];
        Assert.AreEqual("catalog-api", dependency.DependencyName);
        Assert.AreEqual("us-west", dependency.Region);
        Assert.AreEqual("us-east", dependency.Metadata["callerRegion"]);
        Assert.AreEqual("us-west", dependency.Metadata["targetRegion"]);
        Assert.AreEqual("cross-region", dependency.Metadata["networkScope"]);
        Assert.AreEqual("17", dependency.Metadata["injectedDelayMs"]);
        Assert.AreEqual("primary", dependency.Metadata["readSource"]);
    }

    [TestMethod]
    public async Task ProductEndpoint_PerRegionCacheScopes_CanHaveDifferentHitRatesForLocalReads()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);
        await SyncReplicasAsync(repositoryRoot);

        await using CatalogFactory catalogFactory = new(
            repositoryRoot,
            new Dictionary<string, string?>
            {
                [$"{LabConfigurationSections.Regions}:CurrentRegion"] = "us-west",
                [$"{LabConfigurationSections.Regions}:PrimaryRegion"] = "us-east",
                [$"{LabConfigurationSections.Regions}:WestReplicaRegion"] = "us-west"
            });
        using HttpClient catalogClient = catalogFactory.CreateClient();

        await using StorefrontFactory westStorefrontFactory = new(
            repositoryRoot,
            configurationOverrides: new Dictionary<string, string?>
            {
                [$"{LabConfigurationSections.Regions}:CurrentRegion"] = "us-west",
                [$"{LabConfigurationSections.Regions}:PrimaryRegion"] = "us-east",
                [$"{LabConfigurationSections.ServiceEndpoints}:CatalogRegion"] = "us-west"
            },
            configureServices: services =>
            {
                services.AddSingleton<ICatalogProductClient>(new TestCatalogProductClient(catalogClient));
            });
        using HttpClient westStorefrontClient = westStorefrontFactory.CreateClient();

        await using StorefrontFactory eastStorefrontFactory = new(
            repositoryRoot,
            configurationOverrides: new Dictionary<string, string?>
            {
                [$"{LabConfigurationSections.Regions}:CurrentRegion"] = "us-east",
                [$"{LabConfigurationSections.Regions}:PrimaryRegion"] = "us-east",
                [$"{LabConfigurationSections.ServiceEndpoints}:CatalogRegion"] = "us-west"
            },
            configureServices: services =>
            {
                services.AddSingleton<ICatalogProductClient>(new TestCatalogProductClient(catalogClient));
            });
        using HttpClient eastStorefrontClient = eastStorefrontFactory.CreateClient();

        HttpResponseMessage westFirstResponse = await westStorefrontClient.GetAsync("/products/sku-0001?cache=on&readSource=local");
        HttpResponseMessage westSecondResponse = await westStorefrontClient.GetAsync("/products/sku-0001?cache=on&readSource=local");
        HttpResponseMessage eastFirstResponse = await eastStorefrontClient.GetAsync("/products/sku-0001?cache=on&readSource=local");

        Assert.AreEqual(System.Net.HttpStatusCode.OK, westFirstResponse.StatusCode);
        Assert.AreEqual(System.Net.HttpStatusCode.OK, westSecondResponse.StatusCode);
        Assert.AreEqual(System.Net.HttpStatusCode.OK, eastFirstResponse.StatusCode);

        JsonElement westFirstBody = JsonSerializer.Deserialize<JsonElement>(await westFirstResponse.Content.ReadAsStringAsync(), JsonOptions);
        JsonElement westSecondBody = JsonSerializer.Deserialize<JsonElement>(await westSecondResponse.Content.ReadAsStringAsync(), JsonOptions);
        JsonElement eastFirstBody = JsonSerializer.Deserialize<JsonElement>(await eastFirstResponse.Content.ReadAsStringAsync(), JsonOptions);

        Assert.IsFalse(westFirstBody.GetProperty("cache").GetProperty("hit").GetBoolean());
        Assert.IsTrue(westSecondBody.GetProperty("cache").GetProperty("hit").GetBoolean());
        Assert.IsFalse(eastFirstBody.GetProperty("cache").GetProperty("hit").GetBoolean());
        Assert.AreEqual("replica-west", westFirstBody.GetProperty("readSource").GetString());
        Assert.AreEqual("replica-west", westSecondBody.GetProperty("readSource").GetString());
        Assert.AreEqual("replica-west", eastFirstBody.GetProperty("readSource").GetString());

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceRecord[] storefrontTraces = traces
            .Where(trace => trace.Record.Service == "Storefront.Api")
            .Select(trace => trace.Record)
            .ToArray();

        Assert.HasCount(3, storefrontTraces);

        RequestTraceRecord[] westTraces = storefrontTraces.Where(trace => trace.Region == "us-west").ToArray();
        RequestTraceRecord[] eastTraces = storefrontTraces.Where(trace => trace.Region == "us-east").ToArray();

        Assert.HasCount(2, westTraces);
        Assert.HasCount(1, eastTraces);
        Assert.AreEqual(1, westTraces.Count(trace => trace.CacheHit));
        Assert.AreEqual(0, eastTraces.Count(trace => trace.CacheHit));
        Assert.AreEqual("us-west", westTraces[0].StageTimings.Single(stage => stage.StageName == "cache_lookup").Metadata["cacheRegion"]);
        Assert.AreEqual("us-east", eastTraces[0].StageTimings.Single(stage => stage.StageName == "cache_lookup").Metadata["cacheRegion"]);
        Assert.AreEqual("local", westTraces[0].StageTimings.Single(stage => stage.StageName == "cache_lookup").Metadata["requestedReadSource"]);
        Assert.AreEqual("replica-west", westTraces[0].StageTimings.Single(stage => stage.StageName == "cache_lookup").Metadata["effectiveReadSource"]);
        Assert.AreEqual("same-region", westTraces[0].StageTimings.Single(stage => stage.StageName == "cache_lookup").Metadata["selectionScope"]);
    }

    [TestMethod]
    public async Task ProductEndpoint_LocalReplicaUnavailable_FallsBackToPrimaryAndMarksDegradedReadReason()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);
        await SyncReplicasAsync(repositoryRoot);
        await MutatePrimaryProductAsync(repositoryRoot, "sku-0001", productVersion: 47, inventoryVersion: 61, availableQuantity: 5, reservedQuantity: 0);

        await using CatalogFactory westCatalogFactory = new(
            repositoryRoot,
            new Dictionary<string, string?>
            {
                [$"{LabConfigurationSections.Regions}:CurrentRegion"] = "us-west",
                [$"{LabConfigurationSections.Regions}:PrimaryRegion"] = "us-east",
                [$"{LabConfigurationSections.Regions}:WestReplicaRegion"] = "us-west",
                [$"{LabConfigurationSections.RegionalDegradation}:SimulateLocalReplicaUnavailable"] = "true"
            });
        using HttpClient westCatalogClient = westCatalogFactory.CreateClient();

        await using StorefrontFactory storefrontFactory = new(
            repositoryRoot,
            configurationOverrides: new Dictionary<string, string?>
            {
                [$"{LabConfigurationSections.Regions}:CurrentRegion"] = "us-west",
                [$"{LabConfigurationSections.Regions}:PrimaryRegion"] = "us-east",
                [$"{LabConfigurationSections.ServiceEndpoints}:CatalogRegion"] = "us-west",
                [$"{LabConfigurationSections.RegionalDegradation}:SimulateLocalReplicaUnavailable"] = "true"
            },
            configureServices: services =>
            {
                services.AddSingleton<ICatalogProductClient>(new TestCatalogProductClient(westCatalogClient));
            });
        using HttpClient storefrontClient = storefrontFactory.CreateClient();

        HttpResponseMessage response = await storefrontClient.GetAsync("/products/sku-0001?cache=off&readSource=local");

        Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode);

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
        Assert.AreEqual("primary", body.GetProperty("readSource").GetString());
        Assert.AreEqual(47, body.GetProperty("version").GetInt64());

        RequestTraceRecord storefrontTrace = (await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot))
            .Single(trace => trace.Record.Service == "Storefront.Api" &&
                             trace.Record.TraceId == body.GetProperty("request").GetProperty("traceId").GetString())
            .Record;

        StageTimingRecord cacheLookup = storefrontTrace.StageTimings.Single(stage => stage.StageName == "cache_lookup");
        StageTimingRecord catalogCallCompleted = storefrontTrace.StageTimings.Single(stage => stage.StageName == "catalog_call_completed");
        DependencyCallRecord dependency = storefrontTrace.DependencyCalls.Single();

        Assert.AreEqual("primary", cacheLookup.Metadata["effectiveReadSource"]);
        Assert.AreEqual("true", cacheLookup.Metadata["fallbackApplied"]);
        Assert.AreEqual("local_replica_unavailable", cacheLookup.Metadata["fallbackReason"]);
        Assert.AreEqual("primary", catalogCallCompleted.Metadata["readSource"]);
        Assert.AreEqual("local_replica_unavailable", dependency.Metadata["fallbackReason"]);
        Assert.AreEqual("same-region", dependency.Metadata["networkScope"]);
        Assert.IsTrue(storefrontTrace.Notes.Any(note => note.Contains("local_replica_unavailable", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ProductEndpoint_LocalCatalogUnavailable_FailsOverToRemoteCatalogAndRemainsAvailable()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);
        await SyncReplicasAsync(repositoryRoot);

        await using CatalogFactory eastCatalogFactory = new(
            repositoryRoot,
            new Dictionary<string, string?>
            {
                [$"{LabConfigurationSections.Regions}:CurrentRegion"] = "us-east",
                [$"{LabConfigurationSections.Regions}:PrimaryRegion"] = "us-east"
            });
        using HttpClient eastCatalogClient = eastCatalogFactory.CreateClient();

        await using StorefrontFactory storefrontFactory = new(
            repositoryRoot,
            configurationOverrides: new Dictionary<string, string?>
            {
                [$"{LabConfigurationSections.Regions}:CurrentRegion"] = "us-west",
                [$"{LabConfigurationSections.Regions}:PrimaryRegion"] = "us-east",
                [$"{LabConfigurationSections.Regions}:CrossRegionLatencyMs"] = "17",
                [$"{LabConfigurationSections.Regions}:SameRegionLatencyMs"] = "2",
                [$"{LabConfigurationSections.ServiceEndpoints}:CatalogRegion"] = "us-west",
                [$"{LabConfigurationSections.ServiceEndpoints}:CatalogFailoverBaseUrl"] = "http://catalog-east.internal:5203",
                [$"{LabConfigurationSections.ServiceEndpoints}:CatalogFailoverRegion"] = "us-east",
                [$"{LabConfigurationSections.RegionalDegradation}:SimulateLocalCatalogUnavailable"] = "true"
            },
            configureServices: services =>
            {
                services.AddSingleton<ICatalogProductClient>(new RoutedCatalogProductClient(
                    new Dictionary<string, HttpClient>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["us-east"] = eastCatalogClient
                    }));
            });
        using HttpClient storefrontClient = storefrontFactory.CreateClient();

        HttpResponseMessage response = await storefrontClient.GetAsync("/products/sku-0001?cache=off&readSource=local");

        Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode);

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
        Assert.AreEqual("primary", body.GetProperty("readSource").GetString());

        RequestTraceRecord storefrontTrace = (await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot))
            .Single(trace => trace.Record.Service == "Storefront.Api" &&
                             trace.Record.TraceId == body.GetProperty("request").GetProperty("traceId").GetString())
            .Record;

        DependencyCallRecord dependency = storefrontTrace.DependencyCalls.Single();
        StageTimingRecord cacheLookup = storefrontTrace.StageTimings.Single(stage => stage.StageName == "cache_lookup");

        Assert.AreEqual("us-east", dependency.Region);
        Assert.AreEqual("us-east", dependency.Metadata["targetRegion"]);
        Assert.AreEqual("cross-region", dependency.Metadata["networkScope"]);
        Assert.AreEqual("true", dependency.Metadata["degradedModeApplied"]);
        Assert.AreEqual("local_catalog_unavailable", dependency.Metadata["degradedModeReason"]);
        Assert.AreEqual("true", cacheLookup.Metadata["degradedModeApplied"]);
        Assert.AreEqual("local_catalog_unavailable", cacheLookup.Metadata["degradedModeReason"]);
        Assert.IsTrue(storefrontTrace.Notes.Any(note => note.Contains("degraded mode rerouted the Catalog dependency", StringComparison.Ordinal)));
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
        PrimaryDbContextFactory dbContextFactory = new();
        await using PrimaryDbContext dbContext = dbContextFactory.CreateDbContext(Path.Combine(repositoryRoot, "data", "primary.db"));
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

    private static string CreateUniqueTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestCatalogProductClient(HttpClient client) : ICatalogProductClient
    {
        public async Task<HttpResponseMessage> GetProductAsync(
            string productId,
            string runId,
            string correlationId,
            ProductReadSource readSource,
            bool debugTelemetryRequested,
            CatalogDependencyRoutePlan routePlan,
            CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = new(
                HttpMethod.Get,
                $"/catalog/products/{Uri.EscapeDataString(productId)}?readSource={Uri.EscapeDataString(readSource.ToText())}");

            request.Headers.TryAddWithoutValidation(LabHeaderNames.RunId, runId);
            request.Headers.TryAddWithoutValidation(LabHeaderNames.CorrelationId, correlationId);

            if (debugTelemetryRequested)
            {
                request.Headers.TryAddWithoutValidation(LabHeaderNames.DebugTelemetry, "true");
            }

            return await client.SendAsync(request, cancellationToken);
        }
    }

    private sealed class RoutedCatalogProductClient(IReadOnlyDictionary<string, HttpClient> clientsByRegion) : ICatalogProductClient
    {
        public async Task<HttpResponseMessage> GetProductAsync(
            string productId,
            string runId,
            string correlationId,
            ProductReadSource readSource,
            bool debugTelemetryRequested,
            CatalogDependencyRoutePlan routePlan,
            CancellationToken cancellationToken)
        {
            if (!clientsByRegion.TryGetValue(routePlan.EffectiveTargetRegion, out HttpClient? client))
            {
                throw new InvalidOperationException($"No test Catalog client is registered for region '{routePlan.EffectiveTargetRegion}'.");
            }

            using HttpRequestMessage request = new(
                HttpMethod.Get,
                $"/catalog/products/{Uri.EscapeDataString(productId)}?readSource={Uri.EscapeDataString(readSource.ToText())}");

            request.Headers.TryAddWithoutValidation(LabHeaderNames.RunId, runId);
            request.Headers.TryAddWithoutValidation(LabHeaderNames.CorrelationId, correlationId);

            if (debugTelemetryRequested)
            {
                request.Headers.TryAddWithoutValidation(LabHeaderNames.DebugTelemetry, "true");
            }

            return await client.SendAsync(request, cancellationToken);
        }
    }
}
