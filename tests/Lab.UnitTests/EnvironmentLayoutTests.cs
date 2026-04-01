using Lab.Shared.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Lab.UnitTests;

[TestClass]
public sealed class EnvironmentLayoutTests
{
    [TestMethod]
    public void FindRepositoryRoot_WalksUpToTheNearestSolutionMarker()
    {
        string root = CreateUniqueTempDirectory();
        string nested = Path.Combine(root, "src", "Storefront.Api");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(root, "ecommerce-systems-lab.sln"), string.Empty);

        string discoveredRoot = EnvironmentLayout.FindRepositoryRoot(nested);

        Assert.AreEqual(Path.GetFullPath(root), discoveredRoot);
    }

    [TestMethod]
    public void EnvironmentLayout_Create_ResolvesRelativeDefaultsAgainstTheRepositoryRoot()
    {
        string root = CreateUniqueTempDirectory();
        string contentRoot = Path.Combine(root, "src", "Storefront.Api");
        Directory.CreateDirectory(contentRoot);
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "ecommerce-systems-lab.sln"), string.Empty);

        EnvironmentLayout layout = EnvironmentLayout.Create(
            new TestHostEnvironment(contentRoot, "Storefront.Api"),
            new RepositoryOptions(),
            new DatabasePathOptions(),
            new LogPathOptions(),
            new RegionOptions());

        Assert.AreEqual(Path.Combine(root, "data", "primary.db"), layout.PrimaryDatabasePath);
        Assert.AreEqual(Path.Combine(root, "logs", "requests.jsonl"), layout.RequestsJsonlPath);
        Assert.AreEqual(Path.Combine(root, "analysis"), layout.AnalysisDirectory);
        Assert.AreEqual(Path.Combine(root, "logs", "storefront.log"), layout.ServiceLogPath);
        Assert.AreEqual("local", layout.CurrentRegion);
    }

    [TestMethod]
    public void AddLabConfiguration_BindsOverridesAndPublishesResolvedEnvironmentLayout()
    {
        string discoveredRoot = CreateUniqueTempDirectory();
        string contentRoot = Path.Combine(discoveredRoot, "src", "Catalog.Api");
        Directory.CreateDirectory(contentRoot);
        Directory.CreateDirectory(Path.Combine(discoveredRoot, "src"));
        File.WriteAllText(Path.Combine(discoveredRoot, "ecommerce-systems-lab.sln"), string.Empty);

        string configuredRoot = Path.Combine(discoveredRoot, "custom-root");
        Directory.CreateDirectory(configuredRoot);

        Dictionary<string, string?> values = new()
        {
            [$"{LabConfigurationSections.Repository}:RootPath"] = configuredRoot,
            [$"{LabConfigurationSections.DatabasePaths}:Primary"] = "db/primary.sqlite",
            [$"{LabConfigurationSections.LogPaths}:AnalysisDirectory"] = "reports",
            [$"{LabConfigurationSections.Regions}:CurrentRegion"] = "west",
            [$"{LabConfigurationSections.Regions}:SameRegionLatencyMs"] = "3",
            [$"{LabConfigurationSections.Regions}:CrossRegionLatencyMs"] = "29",
            [$"{LabConfigurationSections.RegionalDegradation}:SimulateLocalReplicaUnavailable"] = "true",
            [$"{LabConfigurationSections.RegionalDegradation}:SimulateLocalCatalogUnavailable"] = "true",
            [$"{LabConfigurationSections.ServiceEndpoints}:CatalogBaseUrl"] = "http://catalog.internal:6200",
            [$"{LabConfigurationSections.ServiceEndpoints}:CatalogRegion"] = "us-west",
            [$"{LabConfigurationSections.ServiceEndpoints}:CatalogFailoverBaseUrl"] = "http://catalog-failover.internal:6201",
            [$"{LabConfigurationSections.ServiceEndpoints}:CatalogFailoverRegion"] = "us-east",
            [$"{LabConfigurationSections.ServiceEndpoints}:CartBaseUrl"] = "http://cart.internal:6300",
            [$"{LabConfigurationSections.ServiceEndpoints}:CartRegion"] = "us-east",
            [$"{LabConfigurationSections.ServiceEndpoints}:OrderBaseUrl"] = "http://order.internal:6350",
            [$"{LabConfigurationSections.ServiceEndpoints}:OrderRegion"] = "us-west",
            [$"{LabConfigurationSections.ServiceEndpoints}:PaymentSimulatorBaseUrl"] = "http://payments.internal:6400",
            [$"{LabConfigurationSections.ServiceEndpoints}:PaymentSimulatorRegion"] = "us-east",
            [$"{LabConfigurationSections.Cache}:Enabled"] = "false",
            [$"{LabConfigurationSections.RateLimiter}:TokenBucketCapacity"] = "250",
            [$"{LabConfigurationSections.RateLimiter}:Checkout:TokenBucketCapacity"] = "7",
            [$"{LabConfigurationSections.RateLimiter}:Checkout:TokensPerSecond"] = "3"
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddLabConfiguration(configuration, new TestHostEnvironment(contentRoot, "Catalog.Api"));

        using ServiceProvider serviceProvider = services.BuildServiceProvider();

        EnvironmentLayout layout = serviceProvider.GetRequiredService<EnvironmentLayout>();
        RegionOptions regionOptions = serviceProvider.GetRequiredService<IOptions<RegionOptions>>().Value;
        RegionalDegradationOptions regionalDegradationOptions = serviceProvider.GetRequiredService<IOptions<RegionalDegradationOptions>>().Value;
        ServiceEndpointOptions serviceEndpointOptions = serviceProvider.GetRequiredService<IOptions<ServiceEndpointOptions>>().Value;
        CacheOptions cacheOptions = serviceProvider.GetRequiredService<IOptions<CacheOptions>>().Value;
        RateLimiterOptions rateLimiterOptions = serviceProvider.GetRequiredService<IOptions<RateLimiterOptions>>().Value;

        Assert.AreEqual(Path.Combine(configuredRoot, "db", "primary.sqlite"), layout.PrimaryDatabasePath);
        Assert.AreEqual(Path.Combine(configuredRoot, "reports"), layout.AnalysisDirectory);
        Assert.AreEqual("west", layout.CurrentRegion);
        Assert.AreEqual(3, regionOptions.SameRegionLatencyMs);
        Assert.AreEqual(29, regionOptions.CrossRegionLatencyMs);
        Assert.IsTrue(regionalDegradationOptions.SimulateLocalReplicaUnavailable);
        Assert.IsTrue(regionalDegradationOptions.SimulateLocalCatalogUnavailable);
        Assert.AreEqual("http://catalog.internal:6200", serviceEndpointOptions.CatalogBaseUrl);
        Assert.AreEqual("us-west", serviceEndpointOptions.CatalogRegion);
        Assert.AreEqual("http://catalog-failover.internal:6201", serviceEndpointOptions.CatalogFailoverBaseUrl);
        Assert.AreEqual("us-east", serviceEndpointOptions.CatalogFailoverRegion);
        Assert.AreEqual("http://cart.internal:6300", serviceEndpointOptions.CartBaseUrl);
        Assert.AreEqual("us-east", serviceEndpointOptions.CartRegion);
        Assert.AreEqual("http://order.internal:6350", serviceEndpointOptions.OrderBaseUrl);
        Assert.AreEqual("us-west", serviceEndpointOptions.OrderRegion);
        Assert.AreEqual("http://payments.internal:6400", serviceEndpointOptions.PaymentSimulatorBaseUrl);
        Assert.AreEqual("us-east", serviceEndpointOptions.PaymentSimulatorRegion);
        Assert.IsFalse(cacheOptions.Enabled);
        Assert.AreEqual(250, rateLimiterOptions.TokenBucketCapacity);
        Assert.AreEqual(7, rateLimiterOptions.Checkout.TokenBucketCapacity);
        Assert.AreEqual(3, rateLimiterOptions.Checkout.TokensPerSecond);
    }

    private static string CreateUniqueTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestHostEnvironment(string contentRootPath, string applicationName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = applicationName;

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }
}
