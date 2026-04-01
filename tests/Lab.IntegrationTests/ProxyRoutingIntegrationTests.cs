using System.Net;
using System.Net.Http.Json;
using Lab.Shared.Http;
using Proxy.Configuration;
using Proxy.Forwarding;

namespace Lab.IntegrationTests;

[TestClass]
public sealed class ProxyRoutingIntegrationTests
{
    [TestMethod]
    public async Task Proxy_RoundRobinDistributesAcrossStorefrontBackends()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await using TestBackendHost backendA = await TestBackendHost.StartAsync("backend-a");
        await using TestBackendHost backendB = await TestBackendHost.StartAsync("backend-b");

        using ProxyFactory factory = new(
            repositoryRoot,
            new Dictionary<string, string?>
            {
                ["Lab:Proxy:RoutingMode"] = ProxyRoutingModes.RoundRobin,
                ["Lab:Proxy:Storefront:Backends:0"] = backendA.BaseUri.AbsoluteUri,
                ["Lab:Proxy:Storefront:Backends:1"] = backendB.BaseUri.AbsoluteUri,
                ["Lab:Proxy:Catalog:Enabled"] = "false"
            });

        using HttpClient client = factory.CreateClient();

        List<string?> observedBackends = [];

        for (int index = 0; index < 4; index++)
        {
            using HttpResponseMessage response = await client.GetAsync($"/health?request={index}");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(ProxyRoutingModes.RoundRobin, response.Headers.GetValues(ProxyForwarder.ProxyRoutingModeHeaderName).Single());
            observedBackends.Add(response.Headers.GetValues(ProxyForwarder.ProxyBackendHeaderName).Single());
        }

        CollectionAssert.AreEqual(
            new[]
            {
                backendA.BaseUri.AbsoluteUri,
                backendB.BaseUri.AbsoluteUri,
                backendA.BaseUri.AbsoluteUri,
                backendB.BaseUri.AbsoluteUri
            },
            observedBackends);
    }

    [TestMethod]
    public async Task Proxy_StickyMode_ReusesSameBackendForSameSessionKey()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await using TestBackendHost backendA = await TestBackendHost.StartAsync("backend-a");
        await using TestBackendHost backendB = await TestBackendHost.StartAsync("backend-b");

        using ProxyFactory factory = new(
            repositoryRoot,
            new Dictionary<string, string?>
            {
                ["Lab:Proxy:RoutingMode"] = ProxyRoutingModes.Sticky,
                ["Lab:Proxy:Storefront:Backends:0"] = backendA.BaseUri.AbsoluteUri,
                ["Lab:Proxy:Storefront:Backends:1"] = backendB.BaseUri.AbsoluteUri,
                ["Lab:Proxy:Catalog:Enabled"] = "false"
            });

        using HttpClient client = factory.CreateClient();
        List<string?> observedBackends = [];

        for (int index = 0; index < 3; index++)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, $"/health?request={index}");
            request.Headers.TryAddWithoutValidation(LabHeaderNames.SessionKey, "sticky-header-071");

            using HttpResponseMessage response = await client.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(ProxyRoutingModes.Sticky, response.Headers.GetValues(ProxyForwarder.ProxyRoutingModeHeaderName).Single());
            observedBackends.Add(response.Headers.GetValues(ProxyForwarder.ProxyBackendHeaderName).Single());
        }

        Assert.AreEqual(3, observedBackends.Count);
        Assert.IsTrue(observedBackends.All(backend => backend == observedBackends[0]));
    }

    [TestMethod]
    public async Task Proxy_StickyMode_UsesSessionCookieWhenHeaderMissing()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await using TestBackendHost backendA = await TestBackendHost.StartAsync("backend-a");
        await using TestBackendHost backendB = await TestBackendHost.StartAsync("backend-b");

        using ProxyFactory factory = new(
            repositoryRoot,
            new Dictionary<string, string?>
            {
                ["Lab:Proxy:RoutingMode"] = ProxyRoutingModes.Sticky,
                ["Lab:Proxy:Storefront:Backends:0"] = backendA.BaseUri.AbsoluteUri,
                ["Lab:Proxy:Storefront:Backends:1"] = backendB.BaseUri.AbsoluteUri,
                ["Lab:Proxy:Catalog:Enabled"] = "false"
            });

        using HttpClient client = factory.CreateClient();
        List<string?> observedBackends = [];

        for (int index = 0; index < 2; index++)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, $"/health?request={index}");
            request.Headers.TryAddWithoutValidation("Cookie", $"{LabCookieNames.Session}=sticky-cookie-071");

            using HttpResponseMessage response = await client.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            observedBackends.Add(response.Headers.GetValues(ProxyForwarder.ProxyBackendHeaderName).Single());
        }

        Assert.AreEqual(2, observedBackends.Count);
        Assert.AreEqual(observedBackends[0], observedBackends[1]);
    }

    [TestMethod]
    public async Task Proxy_StickyMode_RemapsAfterBackendFailure()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        TestBackendHost backendA = await TestBackendHost.StartAsync("backend-a");
        TestBackendHost backendB = await TestBackendHost.StartAsync("backend-b");

        try
        {
            using ProxyFactory factory = new(
                repositoryRoot,
                new Dictionary<string, string?>
                {
                    ["Lab:Proxy:RoutingMode"] = ProxyRoutingModes.Sticky,
                    ["Lab:Proxy:Storefront:Backends:0"] = backendA.BaseUri.AbsoluteUri,
                    ["Lab:Proxy:Storefront:Backends:1"] = backendB.BaseUri.AbsoluteUri,
                    ["Lab:Proxy:Catalog:Enabled"] = "false"
                });

            using HttpClient client = factory.CreateClient();

            using HttpRequestMessage firstRequest = new(HttpMethod.Get, "/health?request=1");
            firstRequest.Headers.TryAddWithoutValidation(LabHeaderNames.SessionKey, "sticky-remap-071");
            using HttpResponseMessage firstResponse = await client.SendAsync(firstRequest);

            Assert.AreEqual(HttpStatusCode.OK, firstResponse.StatusCode);
            string firstBackend = firstResponse.Headers.GetValues(ProxyForwarder.ProxyBackendHeaderName).Single();
            Assert.AreEqual(backendA.BaseUri.AbsoluteUri, firstBackend);

            await backendA.StopAsync();

            using HttpRequestMessage secondRequest = new(HttpMethod.Get, "/health?request=2");
            secondRequest.Headers.TryAddWithoutValidation(LabHeaderNames.SessionKey, "sticky-remap-071");
            using HttpResponseMessage secondResponse = await client.SendAsync(secondRequest);

            Assert.AreEqual(HttpStatusCode.OK, secondResponse.StatusCode);
            string secondBackend = secondResponse.Headers.GetValues(ProxyForwarder.ProxyBackendHeaderName).Single();
            Assert.AreEqual(backendB.BaseUri.AbsoluteUri, secondBackend);

            using HttpRequestMessage thirdRequest = new(HttpMethod.Get, "/health?request=3");
            thirdRequest.Headers.TryAddWithoutValidation(LabHeaderNames.SessionKey, "sticky-remap-071");
            using HttpResponseMessage thirdResponse = await client.SendAsync(thirdRequest);

            Assert.AreEqual(HttpStatusCode.OK, thirdResponse.StatusCode);
            string thirdBackend = thirdResponse.Headers.GetValues(ProxyForwarder.ProxyBackendHeaderName).Single();
            Assert.AreEqual(backendB.BaseUri.AbsoluteUri, thirdBackend);
            Assert.AreEqual(ProxyRoutingModes.Sticky, thirdResponse.Headers.GetValues(ProxyForwarder.ProxyRoutingModeHeaderName).Single());
        }
        finally
        {
            await backendA.DisposeAsync();
            await backendB.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task Proxy_ForwardsMethodPathQueryBodyAndSelectedHeaders()
    {
        string repositoryRoot = CreateUniqueTempDirectory();
        await using TestBackendHost backend = await TestBackendHost.StartAsync("backend-only");

        using ProxyFactory factory = new(
            repositoryRoot,
            new Dictionary<string, string?>
            {
                ["Lab:Proxy:Storefront:Backends:0"] = backend.BaseUri.AbsoluteUri,
                ["Lab:Proxy:Catalog:Enabled"] = "false"
            });

        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Post, "/checkout?mode=sync&probe=1")
        {
            Content = JsonContent.Create(new
            {
                userId = "user-0002",
                paymentMode = "fast_success"
            })
        };
        request.Headers.TryAddWithoutValidation("X-Run-Id", "proxy-run");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "proxy-correlation");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "proxy-idempotency");
        request.Headers.TryAddWithoutValidation("X-Session-Key", "proxy-session");
        request.Headers.TryAddWithoutValidation("X-Debug-Telemetry", "true");

        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("storefront", response.Headers.GetValues(ProxyForwarder.ProxyRouteHeaderName).Single());
        Assert.AreEqual(backend.BaseUri.AbsoluteUri, response.Headers.GetValues(ProxyForwarder.ProxyBackendHeaderName).Single());
        Assert.AreEqual(ProxyRoutingModes.RoundRobin, response.Headers.GetValues(ProxyForwarder.ProxyRoutingModeHeaderName).Single());
        Assert.IsTrue(response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies));
        StringAssert.Contains(cookies.Single(), "backend-id=backend-only");

        ProxyEchoResponse? payload = await response.Content.ReadFromJsonAsync<ProxyEchoResponse>();
        Assert.IsNotNull(payload);
        Assert.AreEqual("backend-only", payload.BackendId);
        Assert.AreEqual("POST", payload.Method);
        Assert.AreEqual("/checkout", payload.Path);
        Assert.AreEqual("?mode=sync&probe=1", payload.Query);
        StringAssert.Contains(payload.Body, "\"userId\":\"user-0002\"");
        StringAssert.Contains(payload.Body, "\"paymentMode\":\"fast_success\"");
        Assert.AreEqual("proxy-run", payload.RunId);
        Assert.AreEqual("proxy-correlation", payload.CorrelationId);
        Assert.AreEqual("proxy-idempotency", payload.IdempotencyKey);
        Assert.AreEqual("proxy-session", payload.SessionKey);
        Assert.AreEqual("true", payload.DebugTelemetry);
    }

    private static string CreateUniqueTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed record ProxyEchoResponse(
        string BackendId,
        string Method,
        string Path,
        string Query,
        string Body,
        string? RunId,
        string? CorrelationId,
        string? IdempotencyKey,
        string? SessionKey,
        string? DebugTelemetry);
}
