using Lab.Shared.Configuration;
using Lab.Shared.Networking;
using Storefront.Api.ProductPages;

namespace Lab.UnitTests;

[TestClass]
public sealed class CatalogDependencyRouteResolverTests
{
    [TestMethod]
    public void Resolve_HealthyCatalogPath_UsesRequestedEndpointWithoutDegradedMode()
    {
        ConfiguredRegionNetworkEnvelopePolicy policy = CreatePolicy("us-west", sameRegionLatencyMs: 2, crossRegionLatencyMs: 35);

        CatalogDependencyRoutePlan plan = CatalogDependencyRouteResolver.Resolve(
            callerRegion: "us-west",
            serviceEndpointOptions: new ServiceEndpointOptions
            {
                CatalogBaseUrl = "http://catalog-west.internal:5203",
                CatalogRegion = "us-west",
                CatalogFailoverBaseUrl = "http://catalog-east.internal:5203",
                CatalogFailoverRegion = "us-east"
            },
            degradationOptions: new RegionalDegradationOptions(),
            networkEnvelopePolicy: policy);

        Assert.AreEqual("http://catalog-west.internal:5203/", plan.EffectiveBaseUrl);
        Assert.AreEqual("us-west", plan.EffectiveTargetRegion);
        Assert.AreEqual("same-region", plan.NetworkEnvelope.NetworkScope);
        Assert.AreEqual(2, plan.NetworkEnvelope.InjectedDelayMs);
        Assert.IsFalse(plan.DegradedModeApplied);
        Assert.IsNull(plan.DegradedReason);
    }

    [TestMethod]
    public void Resolve_LocalCatalogUnavailable_ReroutesToFailoverRegion()
    {
        ConfiguredRegionNetworkEnvelopePolicy policy = CreatePolicy("us-west", sameRegionLatencyMs: 2, crossRegionLatencyMs: 35);

        CatalogDependencyRoutePlan plan = CatalogDependencyRouteResolver.Resolve(
            callerRegion: "us-west",
            serviceEndpointOptions: new ServiceEndpointOptions
            {
                CatalogBaseUrl = "http://catalog-west.internal:5203",
                CatalogRegion = "us-west",
                CatalogFailoverBaseUrl = "http://catalog-east.internal:5203",
                CatalogFailoverRegion = "us-east"
            },
            degradationOptions: new RegionalDegradationOptions
            {
                SimulateLocalCatalogUnavailable = true
            },
            networkEnvelopePolicy: policy);

        Assert.AreEqual("http://catalog-west.internal:5203/", plan.RequestedBaseUrl);
        Assert.AreEqual("us-west", plan.RequestedTargetRegion);
        Assert.AreEqual("http://catalog-east.internal:5203/", plan.EffectiveBaseUrl);
        Assert.AreEqual("us-east", plan.EffectiveTargetRegion);
        Assert.AreEqual("cross-region", plan.NetworkEnvelope.NetworkScope);
        Assert.AreEqual(35, plan.NetworkEnvelope.InjectedDelayMs);
        Assert.IsTrue(plan.DegradedModeApplied);
        Assert.AreEqual("local_catalog_unavailable", plan.DegradedReason);
    }

    private static ConfiguredRegionNetworkEnvelopePolicy CreatePolicy(
        string currentRegion,
        int sameRegionLatencyMs,
        int crossRegionLatencyMs)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "src", "Storefront.Api"));
        File.WriteAllText(Path.Combine(root, "ecommerce-systems-lab.sln"), string.Empty);

        EnvironmentLayout layout = EnvironmentLayout.Create(
            new TestHostEnvironment(Path.Combine(root, "src", "Storefront.Api"), currentRegion),
            new RepositoryOptions { RootPath = root },
            new DatabasePathOptions(),
            new LogPathOptions(),
            new RegionOptions { CurrentRegion = currentRegion });

        return new ConfiguredRegionNetworkEnvelopePolicy(
            layout,
            Microsoft.Extensions.Options.Options.Create(new RegionOptions
            {
                CurrentRegion = currentRegion,
                SameRegionLatencyMs = sameRegionLatencyMs,
                CrossRegionLatencyMs = crossRegionLatencyMs
            }));
    }

    private sealed class TestHostEnvironment(string contentRootPath, string applicationName) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = applicationName;

        public string ContentRootPath { get; set; } = contentRootPath;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.PhysicalFileProvider(contentRootPath);
    }
}
