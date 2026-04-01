using Lab.Analysis.Services;
using Lab.Persistence;
using Lab.Persistence.Replication;
using Lab.Persistence.Seeding;
using Lab.Shared.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Storefront.Api.ProductPages;

namespace Lab.IntegrationTests;

[TestClass]
public sealed class StorefrontTraceAnalysisTests
{
    [TestMethod]
    public async Task Milestone1CpuAndIoTraces_CanBeAnalyzedWithoutSpecialCases()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        await using StorefrontFactory factory = new(repositoryRoot);
        using HttpClient client = factory.CreateClient();

        _ = await client.GetAsync("/cpu?workFactor=2&iterations=20");
        _ = await client.GetAsync("/cpu?workFactor=0&iterations=20");
        _ = await client.GetAsync("/io?delayMs=10&jitterMs=0");
        _ = await client.GetAsync("/io?delayMs=-1&jitterMs=0");

        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => { });
        TelemetryAnalyzer analyzer = new(
            new TelemetryJsonlReader(loggerFactory.CreateLogger<TelemetryJsonlReader>()),
            new QueueMetricsReader(),
            TimeProvider.System);

        string requestsPath = Path.Combine(repositoryRoot, "logs", "requests.jsonl");
        string jobsPath = Path.Combine(repositoryRoot, "logs", "jobs.jsonl");

        var summary = await analyzer.AnalyzeAsync(requestsPath, jobsPath);

        Assert.AreEqual(4, summary.Requests.RequestCount);
        Assert.AreEqual(2, summary.Requests.ErrorCounts["400"]);
        Assert.IsNotNull(summary.Requests.AverageLatencyMs);
        Assert.IsNotNull(summary.Requests.AverageConcurrency);
    }

    [TestMethod]
    public async Task ReplicaStaleReadTraces_AreQuantifiedByFreshnessAnalysis()
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

        HttpResponseMessage response = await storefrontClient.GetAsync("/products/sku-0001?cache=off&readSource=replica-east");
        Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode);

        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => { });
        TelemetryAnalyzer analyzer = new(
            new TelemetryJsonlReader(loggerFactory.CreateLogger<TelemetryJsonlReader>()),
            new QueueMetricsReader(),
            TimeProvider.System);

        string requestsPath = Path.Combine(repositoryRoot, "logs", "requests.jsonl");
        string jobsPath = Path.Combine(repositoryRoot, "logs", "jobs.jsonl");

        var summary = await analyzer.AnalyzeAsync(
            requestsPath,
            jobsPath,
            filter: new Lab.Analysis.Models.AnalysisFilter(null, null, null, "product-page"));

        Assert.IsGreaterThanOrEqualTo(1, summary.ReadFreshness.ReadRequestCount);
        Assert.IsGreaterThanOrEqualTo(1, summary.ReadFreshness.StaleRequestCount);
        Assert.IsGreaterThan(0d, summary.ReadFreshness.StaleRequestFraction!.Value);
        Assert.IsGreaterThan(0d, summary.ReadFreshness.MaxObservedStalenessAgeMs!.Value);
        Assert.IsTrue(summary.ReadFreshness.Sources.Any(source => source.ReadSource == "replica-east"));
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
}
