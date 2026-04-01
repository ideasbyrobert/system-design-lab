using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Proxy.Configuration;
using Proxy.Routing;

namespace Lab.UnitTests;

[TestClass]
public sealed class ProxyRoutingTests
{
    [TestMethod]
    public void RoundRobinBackendSelector_CyclesThroughBackendsPerRoute()
    {
        RoundRobinBackendSelector selector = new();
        Uri backendA = new("http://127.0.0.1:6001/");
        Uri backendB = new("http://127.0.0.1:6002/");

        Uri first = selector.SelectBackend("storefront", [backendA, backendB]);
        Uri second = selector.SelectBackend("storefront", [backendA, backendB]);
        Uri third = selector.SelectBackend("storefront", [backendA, backendB]);
        Uri fourth = selector.SelectBackend("storefront", [backendA, backendB]);

        Assert.AreEqual(backendA, first);
        Assert.AreEqual(backendB, second);
        Assert.AreEqual(backendA, third);
        Assert.AreEqual(backendB, fourth);
    }

    [TestMethod]
    public void ProxyRoutingService_PrefersCatalogPrefixAndBuildsForwardUri()
    {
        ProxyOptions options = new()
        {
            RoutingMode = ProxyRoutingModes.RoundRobin,
            Storefront = new ProxyBackendGroupOptions
            {
                PathPrefix = "/",
                Backends = ["http://127.0.0.1:7001"]
            },
            Catalog = new ProxyBackendGroupOptions
            {
                PathPrefix = "/catalog",
                Backends = ["http://127.0.0.1:7002"]
            }
        };

        ProxyRoutingService routingService = CreateRoutingService(options);

        bool matched = routingService.TryResolve(
            new ProxyRoutingInput(
                RequestPath: "/catalog/products/sku-0001",
                QueryString: new QueryString("?cache=off"),
                SessionKeyHeader: null,
                SessionCookie: null),
            out ProxyRoutingDecision? decision,
            out string? error);

        Assert.IsTrue(matched, error);
        Assert.IsNotNull(decision);
        Assert.AreEqual("catalog", decision.RouteName);
        Assert.AreEqual(ProxyRoutingModes.RoundRobin, decision.RoutingMode);
        Assert.AreEqual("round_robin", decision.BackendSelection);
        Assert.AreEqual("http://127.0.0.1:7002/catalog/products/sku-0001?cache=off", decision.ForwardUri.AbsoluteUri);
    }

    [TestMethod]
    public void ProxyRoutingService_StickyMode_ReusesAssignedBackendForSameSessionKey()
    {
        ProxyOptions options = new()
        {
            RoutingMode = ProxyRoutingModes.Sticky,
            Storefront = new ProxyBackendGroupOptions
            {
                PathPrefix = "/",
                Backends =
                [
                    "http://127.0.0.1:7101",
                    "http://127.0.0.1:7102"
                ]
            },
            Catalog = new ProxyBackendGroupOptions
            {
                Enabled = false
            }
        };

        ProxyRoutingService routingService = CreateRoutingService(options);
        ProxyRoutingInput input = new(
            RequestPath: "/cart/items",
            QueryString: QueryString.Empty,
            SessionKeyHeader: "sess-071",
            SessionCookie: null);

        Assert.IsTrue(routingService.TryResolve(input, out ProxyRoutingDecision? firstDecision, out string? firstError), firstError);
        Assert.IsTrue(routingService.TryResolve(input, out ProxyRoutingDecision? secondDecision, out string? secondError), secondError);

        Assert.IsNotNull(firstDecision);
        Assert.IsNotNull(secondDecision);
        Assert.AreEqual(ProxyRoutingModes.Sticky, firstDecision.RoutingMode);
        Assert.AreEqual("sticky_assigned", firstDecision.BackendSelection);
        Assert.AreEqual("sticky_reused", secondDecision.BackendSelection);
        Assert.AreEqual(firstDecision.BackendBaseUri, secondDecision.BackendBaseUri);
        Assert.AreEqual("header", secondDecision.SessionKeySource);
        Assert.AreEqual("sess-071", secondDecision.SessionKey);
        Assert.AreEqual(1, routingService.StickyAssignmentCount);
    }

    [TestMethod]
    public void ProxyRoutingService_StickyMode_RemapClearsFailedAssignmentAndChoosesAlternateBackend()
    {
        ProxyOptions options = new()
        {
            RoutingMode = ProxyRoutingModes.Sticky,
            Storefront = new ProxyBackendGroupOptions
            {
                PathPrefix = "/",
                Backends =
                [
                    "http://127.0.0.1:7201",
                    "http://127.0.0.1:7202"
                ]
            },
            Catalog = new ProxyBackendGroupOptions
            {
                Enabled = false
            }
        };

        ProxyRoutingService routingService = CreateRoutingService(options);
        ProxyRoutingInput input = new(
            RequestPath: "/cart/items",
            QueryString: QueryString.Empty,
            SessionKeyHeader: null,
            SessionCookie: "sess-cookie-071");

        Assert.IsTrue(routingService.TryResolve(input, out ProxyRoutingDecision? originalDecision, out string? originalError), originalError);
        Assert.IsNotNull(originalDecision);

        bool remapped = routingService.TryRemapAfterTransportFailure(
            originalDecision,
            out ProxyRoutingDecision? remappedDecision,
            out string? remapError);

        Assert.IsTrue(remapped, remapError);
        Assert.IsNotNull(remappedDecision);
        Assert.AreEqual(ProxyRoutingModes.Sticky, remappedDecision.RoutingMode);
        Assert.AreEqual("sticky_remapped_after_failure", remappedDecision.BackendSelection);
        Assert.AreEqual("cookie", remappedDecision.SessionKeySource);
        Assert.AreEqual("sess-cookie-071", remappedDecision.SessionKey);
        Assert.AreNotEqual(originalDecision.BackendBaseUri, remappedDecision.BackendBaseUri);

        Assert.IsTrue(routingService.TryResolve(input, out ProxyRoutingDecision? followupDecision, out string? followupError), followupError);
        Assert.IsNotNull(followupDecision);
        Assert.AreEqual(remappedDecision.BackendBaseUri, followupDecision.BackendBaseUri);
        Assert.AreEqual("sticky_reused", followupDecision.BackendSelection);
    }

    [TestMethod]
    public void ProxyOptionsParser_AllowsHigherPrecedenceBackendsToReplaceDefaults_AndParsesStickyMode()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lab:Proxy:RoutingMode"] = "round_robin",
                ["Lab:Proxy:Storefront:PathPrefix"] = "/",
                ["Lab:Proxy:Storefront:Backends:0"] = "http://127.0.0.1:5088",
                ["Lab:Proxy:Catalog:PathPrefix"] = "/catalog",
                ["Lab:Proxy:Catalog:Backends:0"] = "http://127.0.0.1:5084"
            })
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lab:Proxy:RoutingMode"] = "sticky",
                ["Lab:Proxy:Storefront:Backends:0"] = "http://127.0.0.1:5091",
                ["Lab:Proxy:Storefront:Backends:1"] = "http://127.0.0.1:5092"
            })
            .Build();

        ProxyOptions options = new();
        ProxyOptionsParser.Apply(options, configuration.GetSection("Lab:Proxy"));

        Assert.AreEqual(ProxyRoutingModes.Sticky, options.RoutingMode);
        CollectionAssert.AreEqual(
            new[]
            {
                "http://127.0.0.1:5091",
                "http://127.0.0.1:5092"
            },
            options.Storefront.Backends);
    }

    [TestMethod]
    public void ProxySessionKeyResolver_PrefersHeaderOverCookie()
    {
        ProxySessionKeyResolution resolution = ProxySessionKeyResolver.Resolve("header-key", "cookie-key");

        Assert.AreEqual("header-key", resolution.SessionKey);
        Assert.AreEqual(ProxySessionKeySources.Header, resolution.Source);
    }

    private static ProxyRoutingService CreateRoutingService(ProxyOptions options) =>
        new(
            Options.Create(options),
            new RoundRobinBackendSelector(),
            new StickyBackendAssignmentStore());
}
