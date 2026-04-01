using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Lab.Persistence;
using Lab.Persistence.Seeding;
using Lab.Shared.Http;
using Lab.Telemetry.RequestTracing;
using Microsoft.Extensions.DependencyInjection;
using Storefront.Api.CartState;

namespace Lab.IntegrationTests;

[TestClass]
public sealed class StorefrontCartEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task CartEndpoint_WorksThroughStorefront_RecordsDependencyTiming_AndValidatesReturnedCart()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await SeedPrimaryDatabaseAsync(repositoryRoot, productCount: 4, userCount: 2);

        await using CartFactory cartFactory = new(repositoryRoot);
        using HttpClient cartHttpClient = cartFactory.CreateClient();
        await using StorefrontFactory storefrontFactory = new(
            repositoryRoot,
            configureServices: services =>
            {
                services.AddSingleton<ICartClient>(new TestCartClient(cartHttpClient));
            });
        using HttpClient storefrontClient = storefrontFactory.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Post, "/cart/items")
        {
            Content = JsonContent.Create(new
            {
                userId = "user-0001",
                productId = "sku-0001",
                quantity = 2
            })
        };

        HttpResponseMessage response = await storefrontClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
        string storefrontTraceId = body.GetProperty("request").GetProperty("traceId").GetString()!;

        Assert.AreEqual("user-0001", body.GetProperty("userId").GetString());
        Assert.AreEqual("cart-api", body.GetProperty("source").GetString());
        Assert.AreEqual("added", body.GetProperty("mutationOutcome").GetString());
        Assert.AreEqual(1, body.GetProperty("distinctItemCount").GetInt32());
        Assert.AreEqual(2, body.GetProperty("totalQuantity").GetInt32());
        Assert.AreEqual(2272, body.GetProperty("totalPriceCents").GetInt32());
        Assert.AreEqual("Cart.Api", body.GetProperty("cart").GetProperty("service").GetString());
        Assert.AreEqual("sku-0001", body.GetProperty("items")[0].GetProperty("productId").GetString());
        Assert.AreEqual(2, body.GetProperty("items")[0].GetProperty("quantity").GetInt32());
        Assert.AreEqual(1136, body.GetProperty("items")[0].GetProperty("unitPriceSnapshotCents").GetInt32());

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceRecord storefrontTrace = traces.Single(trace => trace.Record.TraceId == storefrontTraceId).Record;
        RequestTraceRecord cartTrace = traces.Select(trace => trace.Record).Single(trace => trace.Service == "Cart.Api");

        Assert.AreEqual("add-item-to-cart", storefrontTrace.Operation);
        Assert.AreEqual("Storefront.Api", storefrontTrace.Service);
        Assert.AreEqual("/cart/items", storefrontTrace.Route);
        Assert.AreEqual("user-0001", storefrontTrace.UserId);
        Assert.IsTrue(storefrontTrace.ContractSatisfied);
        Assert.AreEqual(200, storefrontTrace.StatusCode);
        Assert.HasCount(1, storefrontTrace.DependencyCalls);
        Assert.AreEqual("cart-api", storefrontTrace.DependencyCalls[0].DependencyName);
        Assert.AreEqual(200, storefrontTrace.DependencyCalls[0].StatusCode);
        Assert.AreEqual("success", storefrontTrace.StageTimings.Single(stage => stage.StageName == "cart_call_completed").Outcome);
        CollectionAssert.AreEquivalent(
            new[]
            {
                "request_received",
                "cart_call_started",
                "cart_call_completed",
                "response_sent",
                "http_request"
            },
            storefrontTrace.StageTimings.Select(stage => stage.StageName).ToArray());

        Assert.AreEqual("cart-add-item", cartTrace.Operation);
        Assert.AreEqual(storefrontTrace.CorrelationId, cartTrace.CorrelationId);
    }

    [TestMethod]
    public async Task CartEndpoint_WithoutSessionKey_EmitsHeaderAndCookie_AndRecordsGeneratedKey()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        await using StorefrontFactory storefrontFactory = new(repositoryRoot);
        using HttpClient storefrontClient = storefrontFactory.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Post, "/cart/items")
        {
            Content = JsonContent.Create(new
            {
                userId = "user-0001",
                productId = "sku-0001",
                quantity = 0
            })
        };

        HttpResponseMessage response = await storefrontClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.IsTrue(response.Headers.Contains(LabHeaderNames.SessionKey));

        string sessionKey = response.Headers.GetValues(LabHeaderNames.SessionKey).Single();
        Assert.IsTrue(sessionKey.StartsWith("sess-", StringComparison.Ordinal));
        Assert.IsTrue(response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? setCookieValues));
        StringAssert.Contains(setCookieValues.Single(), $"{LabCookieNames.Session}={sessionKey}");

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceRecord storefrontTrace = traces.Single().Record;
        StageTimingRecord requestReceived = storefrontTrace.StageTimings.Single(stage => stage.StageName == "request_received");

        Assert.AreEqual(sessionKey, storefrontTrace.SessionKey);
        Assert.AreEqual(sessionKey, requestReceived.Metadata["sessionKey"]);
        Assert.AreEqual("generated", requestReceived.Metadata["sessionKeySource"]);
        Assert.AreEqual("true", requestReceived.Metadata["sessionCookieIssued"]);
        CollectionAssert.Contains(storefrontTrace.Notes.ToArray(), "Storefront generated a session key because the request did not include X-Session-Key or lab-session.");
    }

    [TestMethod]
    public async Task CartEndpoint_WithSessionKeyHeader_PreservesHeaderValue_WithoutIssuingCookie()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        await using StorefrontFactory storefrontFactory = new(repositoryRoot);
        using HttpClient storefrontClient = storefrontFactory.CreateClient();
        const string sessionKey = "session-header-031";

        using HttpRequestMessage request = new(HttpMethod.Post, "/cart/items")
        {
            Content = JsonContent.Create(new
            {
                userId = "user-0001",
                productId = "sku-0001",
                quantity = 0
            })
        };
        request.Headers.TryAddWithoutValidation(LabHeaderNames.SessionKey, sessionKey);

        HttpResponseMessage response = await storefrontClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(sessionKey, response.Headers.GetValues(LabHeaderNames.SessionKey).Single());
        Assert.IsFalse(response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? setCookieValues) &&
                       setCookieValues.Any(value => value.Contains($"{LabCookieNames.Session}=", StringComparison.Ordinal)));

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceRecord storefrontTrace = traces.Single().Record;
        StageTimingRecord requestReceived = storefrontTrace.StageTimings.Single(stage => stage.StageName == "request_received");

        Assert.AreEqual(sessionKey, storefrontTrace.SessionKey);
        Assert.AreEqual(sessionKey, requestReceived.Metadata["sessionKey"]);
        Assert.AreEqual("header", requestReceived.Metadata["sessionKeySource"]);
        Assert.AreEqual("false", requestReceived.Metadata["sessionCookieIssued"]);
    }

    [TestMethod]
    public async Task CartEndpoint_WithSessionKeyCookie_UsesCookieValue_WithoutIssuingReplacementCookie()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        await using StorefrontFactory storefrontFactory = new(repositoryRoot);
        using HttpClient storefrontClient = storefrontFactory.CreateClient();
        const string sessionKey = "session-cookie-031";

        using HttpRequestMessage request = new(HttpMethod.Post, "/cart/items")
        {
            Content = JsonContent.Create(new
            {
                userId = "user-0001",
                productId = "sku-0001",
                quantity = 0
            })
        };
        request.Headers.TryAddWithoutValidation("Cookie", $"{LabCookieNames.Session}={sessionKey}");

        HttpResponseMessage response = await storefrontClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(sessionKey, response.Headers.GetValues(LabHeaderNames.SessionKey).Single());
        Assert.IsFalse(response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? setCookieValues) &&
                       setCookieValues.Any(value => value.Contains($"{LabCookieNames.Session}=", StringComparison.Ordinal)));

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceRecord storefrontTrace = traces.Single().Record;
        StageTimingRecord requestReceived = storefrontTrace.StageTimings.Single(stage => stage.StageName == "request_received");

        Assert.AreEqual(sessionKey, storefrontTrace.SessionKey);
        Assert.AreEqual(sessionKey, requestReceived.Metadata["sessionKey"]);
        Assert.AreEqual("cookie", requestReceived.Metadata["sessionKeySource"]);
        Assert.AreEqual("false", requestReceived.Metadata["sessionCookieIssued"]);
    }

    [TestMethod]
    public async Task CartEndpoint_InvalidQuantity_FailsAtStorefrontBoundaryWithoutDownstreamCall()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        await using StorefrontFactory storefrontFactory = new(repositoryRoot);
        using HttpClient storefrontClient = storefrontFactory.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Post, "/cart/items")
        {
            Content = JsonContent.Create(new
            {
                userId = "user-0001",
                productId = "sku-0001",
                quantity = 0
            })
        };

        HttpResponseMessage response = await storefrontClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
        Assert.AreEqual("invalid_quantity", body.GetProperty("error").GetString());

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceRecord storefrontTrace = traces.Single().Record;

        Assert.AreEqual("add-item-to-cart", storefrontTrace.Operation);
        Assert.AreEqual("user-0001", storefrontTrace.UserId);
        Assert.AreEqual("invalid_quantity", storefrontTrace.ErrorCode);
        Assert.IsTrue(storefrontTrace.ContractSatisfied);
        Assert.AreEqual(400, storefrontTrace.StatusCode);
        Assert.IsEmpty(storefrontTrace.DependencyCalls);
        Assert.AreEqual("validation_failed", storefrontTrace.StageTimings.Single(stage => stage.StageName == "response_sent").Outcome);
    }

    [TestMethod]
    public async Task CartEndpoint_DownstreamContractViolation_IsDistinguishedFromTechnicalFailure()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        await using StorefrontFactory storefrontFactory = new(
            repositoryRoot,
            configureServices: services =>
            {
                services.AddSingleton<ICartClient>(new FakeCartClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "cartId": "cart-1",
                          "userId": "user-0001",
                          "region": "local",
                          "exists": true,
                          "status": "active",
                          "loadOutcome": "loaded",
                          "mutationOutcome": "added",
                          "persisted": true,
                          "distinctItemCount": 0,
                          "totalQuantity": 0,
                          "totalPriceCents": 0,
                          "items": [],
                          "request": {
                            "runId": "cart-run",
                            "traceId": "cart-trace",
                            "requestId": "cart-request",
                            "correlationId": "cart-correlation"
                          }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                })));
            });
        using HttpClient storefrontClient = storefrontFactory.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Post, "/cart/items")
        {
            Content = JsonContent.Create(new
            {
                userId = "user-0001",
                productId = "sku-0001",
                quantity = 2
            })
        };

        HttpResponseMessage response = await storefrontClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
        Assert.AreEqual("cart_contract_violation", body.GetProperty("error").GetString());

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceRecord storefrontTrace = traces.Single().Record;

        Assert.AreEqual("cart_contract_violation", storefrontTrace.ErrorCode);
        Assert.IsFalse(storefrontTrace.ContractSatisfied);
        Assert.AreEqual(502, storefrontTrace.StatusCode);
        Assert.HasCount(1, storefrontTrace.DependencyCalls);
        Assert.AreEqual("success", storefrontTrace.StageTimings.Single(stage => stage.StageName == "cart_call_completed").Outcome);
        Assert.AreEqual("contract_failure", storefrontTrace.StageTimings.Single(stage => stage.StageName == "response_sent").Outcome);
    }

    [TestMethod]
    public async Task CartEndpoint_DownstreamTransportFailure_HasDistinctErrorCode()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        await using StorefrontFactory storefrontFactory = new(
            repositoryRoot,
            configureServices: services =>
            {
                services.AddSingleton<ICartClient>(new FakeCartClient(_ => throw new HttpRequestException("boom")));
            });
        using HttpClient storefrontClient = storefrontFactory.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Post, "/cart/items")
        {
            Content = JsonContent.Create(new
            {
                userId = "user-0001",
                productId = "sku-0001",
                quantity = 2
            })
        };

        HttpResponseMessage response = await storefrontClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
        Assert.AreEqual("cart_transport_error", body.GetProperty("error").GetString());

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceRecord storefrontTrace = traces.Single().Record;

        Assert.AreEqual("cart_transport_error", storefrontTrace.ErrorCode);
        Assert.IsFalse(storefrontTrace.ContractSatisfied);
        Assert.AreEqual(502, storefrontTrace.StatusCode);
        Assert.HasCount(1, storefrontTrace.DependencyCalls);
        Assert.AreEqual("transport_error", storefrontTrace.StageTimings.Single(stage => stage.StageName == "cart_call_completed").Outcome);
        Assert.AreEqual("upstream_failure", storefrontTrace.StageTimings.Single(stage => stage.StageName == "response_sent").Outcome);
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

    private static string CreateUniqueTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestCartClient(HttpClient client) : ICartClient
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

            message.Headers.TryAddWithoutValidation("X-Run-Id", runId);
            message.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);

            return await client.SendAsync(message, cancellationToken);
        }
    }

    private sealed class FakeCartClient(Func<StorefrontCartMutationRequest, Task<HttpResponseMessage>> handler) : ICartClient
    {
        public Task<HttpResponseMessage> AddItemAsync(
            StorefrontCartMutationRequest request,
            string runId,
            string correlationId,
            CancellationToken cancellationToken) => handler(request);
    }
}
