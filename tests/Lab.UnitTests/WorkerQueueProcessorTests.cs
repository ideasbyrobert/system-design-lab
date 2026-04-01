using System.Text.Json;
using Lab.Persistence;
using Lab.Persistence.DependencyInjection;
using Lab.Persistence.Entities;
using Lab.Persistence.Projections;
using Lab.Persistence.Queueing;
using Lab.Persistence.Seeding;
using Lab.Shared.Configuration;
using Lab.Shared.Queueing;
using Lab.Telemetry.RequestTracing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Worker;
using Worker.DependencyInjection;

namespace Lab.UnitTests;

[TestClass]
public sealed class WorkerQueueProcessorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task ProcessAvailableJobsAsync_ProductPageProjectionRebuild_CompletesJobAndWritesJobTrace()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        using IHost host = CreateWorkerHost(repositoryRoot);
        EnvironmentLayout layout = host.Services.GetRequiredService<EnvironmentLayout>();
        SqliteSeedDataService seeder = host.Services.GetRequiredService<SqliteSeedDataService>();

        await seeder.SeedAsync(
            layout.PrimaryDatabasePath,
            new SeedCounts(ProductCount: 2, UserCount: 1),
            resetExisting: true);

        DateTimeOffset enqueuedUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        await EnqueueJobAsync(
            host,
            "job-051-product-001",
            LabQueueJobTypes.ProductPageProjectionRebuild,
            JsonSerializer.Serialize(new ProductPageProjectionRebuildJobPayload("local", "worker-run-051-product"), JsonOptions),
            enqueuedUtc);

        WorkerQueueProcessor processor = host.Services.GetRequiredService<WorkerQueueProcessor>();
        int processedCount = await processor.ProcessAvailableJobsAsync(CancellationToken.None);

        Assert.AreEqual(1, processedCount);

        QueueJobRecord queueJob = await GetRequiredJobAsync(host, "job-051-product-001");
        Assert.AreEqual(QueueJobStatuses.Completed, queueJob.Status);

        ReadModelDbContextFactory readModelFactory = host.Services.GetRequiredService<ReadModelDbContextFactory>();
        await using ReadModelDbContext readModelDbContext = readModelFactory.CreateDbContext(layout.ReadModelDatabasePath);

        Assert.AreEqual(2, await readModelDbContext.ProductPages.CountAsync());

        JobTraceRecord trace = (await ReadJobTracesAsync(repositoryRoot)).Single(item => item.JobId == "job-051-product-001");
        Assert.AreEqual("worker-run-051-product", trace.RunId);
        Assert.AreEqual(LabQueueJobTypes.ProductPageProjectionRebuild, trace.JobType);
        Assert.AreEqual(QueueJobStatuses.Completed, trace.Status);
        Assert.IsTrue(trace.ContractSatisfied);
        Assert.IsGreaterThanOrEqualTo(0d, trace.QueueDelayMs ?? -1d);
        Assert.IsGreaterThanOrEqualTo(0d, trace.ExecutionMs ?? -1d);
        Assert.IsTrue(trace.StageTimings.Any(stage => stage.StageName == "product_page_projection_rebuilt"));
    }

    [TestMethod]
    public async Task ProcessAvailableJobsAsync_OrderHistoryProjectionUpdate_CompletesProjectionJobAndWritesReadModelRow()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        using IHost host = CreateWorkerHost(repositoryRoot);
        EnvironmentLayout layout = host.Services.GetRequiredService<EnvironmentLayout>();
        SqliteSeedDataService seeder = host.Services.GetRequiredService<SqliteSeedDataService>();

        await seeder.SeedAsync(
            layout.PrimaryDatabasePath,
            new SeedCounts(ProductCount: 1, UserCount: 1),
            resetExisting: true);

        await using (PrimaryDbContext dbContext = host.Services.GetRequiredService<PrimaryDbContextFactory>().CreateDbContext(layout.PrimaryDatabasePath))
        {
            dbContext.Orders.Add(new Order
            {
                OrderId = "order-051-history-001",
                UserId = "user-0001",
                CartId = "cart-051-history-001",
                Region = "local",
                Status = "PendingPayment",
                TotalPriceCents = 2199,
                CreatedUtc = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
                SubmittedUtc = new DateTimeOffset(2026, 4, 1, 0, 1, 0, TimeSpan.Zero)
            });
            dbContext.OrderItems.Add(new OrderItem
            {
                OrderItemId = "oi-051-history-001",
                OrderId = "order-051-history-001",
                ProductId = "sku-0001",
                Quantity = 1,
                UnitPriceCents = 1136
            });
            dbContext.Payments.Add(new Payment
            {
                PaymentId = "payment-051-history-001",
                OrderId = "order-051-history-001",
                Provider = "PaymentSimulator",
                IdempotencyKey = "idem-051-history-001",
                Mode = "slow_success",
                Status = "Pending",
                AmountCents = 1136,
                AttemptedUtc = new DateTimeOffset(2026, 4, 1, 0, 1, 30, TimeSpan.Zero)
            });

            await dbContext.SaveChangesAsync();
        }

        await EnqueueJobAsync(
            host,
            "job-051-history-001",
            LabQueueJobTypes.OrderHistoryProjectionUpdate,
            JsonSerializer.Serialize(new OrderHistoryProjectionUpdateJobPayload("order-051-history-001", "user-0001", "worker-run-051-history"), JsonOptions),
            DateTimeOffset.UtcNow.AddMilliseconds(-500));

        WorkerQueueProcessor processor = host.Services.GetRequiredService<WorkerQueueProcessor>();
        int processedCount = await processor.ProcessAvailableJobsAsync(CancellationToken.None);

        Assert.AreEqual(1, processedCount);

        QueueJobRecord queueJob = await GetRequiredJobAsync(host, "job-051-history-001");
        Assert.AreEqual(QueueJobStatuses.Completed, queueJob.Status);

        ReadModelDbContextFactory readModelFactory = host.Services.GetRequiredService<ReadModelDbContextFactory>();
        await using ReadModelDbContext readModelDbContext = readModelFactory.CreateDbContext(layout.ReadModelDatabasePath);
        ReadModelOrderHistory projectionRow = await readModelDbContext.OrderHistories
            .AsNoTracking()
            .SingleAsync(row => row.OrderId == "order-051-history-001");
        OrderHistoryProjectionSummary projectionSummary = JsonSerializer.Deserialize<OrderHistoryProjectionSummary>(projectionRow.SummaryJson, JsonOptions)
            ?? throw new InvalidOperationException("Order-history projection summary JSON could not be deserialized.");

        Assert.AreEqual("PendingPayment", projectionRow.Status);
        Assert.AreEqual("user-0001", projectionRow.UserId);
        Assert.AreEqual("Pending", projectionSummary.Payment?.Status);
        Assert.AreEqual("Sample Product 0001", projectionSummary.Items.Single().ProductName);

        JobTraceRecord trace = (await ReadJobTracesAsync(repositoryRoot)).Single(item => item.JobId == "job-051-history-001");
        Assert.AreEqual(LabQueueJobTypes.OrderHistoryProjectionUpdate, trace.JobType);
        Assert.AreEqual(QueueJobStatuses.Completed, trace.Status);
        Assert.IsTrue(trace.ContractSatisfied);
        Assert.IsTrue(trace.StageTimings.Any(stage => stage.StageName == "order_history_projection_update" && stage.Outcome == "updated"));
        Assert.IsTrue(trace.Notes.Any(note => note.Contains("refreshed the order-history projection row", StringComparison.Ordinal)));
    }

    private static IHost CreateWorkerHost(string repositoryRoot)
    {
        HostApplicationBuilderSettings settings = new()
        {
            ApplicationName = "Worker",
            ContentRootPath = AppContext.BaseDirectory
        };

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(settings);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Lab:Repository:RootPath"] = repositoryRoot,
            ["Lab:ServiceEndpoints:PaymentSimulatorBaseUrl"] = "http://127.0.0.1:65530"
        });
        builder.Services.AddLabConfiguration(builder.Configuration, builder.Environment);
        builder.Services.AddPrimaryPersistence();
        builder.Services.AddReadModelPersistence();
        builder.Services.AddLabWorkerProcessing();

        return builder.Build();
    }

    private static async Task EnqueueJobAsync(
        IHost host,
        string jobId,
        string jobType,
        string payloadJson,
        DateTimeOffset enqueuedUtc)
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        IDurableQueueStore queueStore = scope.ServiceProvider.GetRequiredService<IDurableQueueStore>();

        await queueStore.EnqueueAsync(
            new EnqueueQueueJobRequest(
                QueueJobId: jobId,
                JobType: jobType,
                PayloadJson: payloadJson,
                EnqueuedUtc: enqueuedUtc));
    }

    private static async Task<QueueJobRecord> GetRequiredJobAsync(IHost host, string jobId)
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        IDurableQueueStore queueStore = scope.ServiceProvider.GetRequiredService<IDurableQueueStore>();
        QueueJobRecord? job = await queueStore.GetByIdAsync(jobId);
        return job ?? throw new InvalidOperationException($"Queue job '{jobId}' was not found.");
    }

    private static async Task<IReadOnlyList<JobTraceRecord>> ReadJobTracesAsync(string repositoryRoot)
    {
        string path = Path.Combine(repositoryRoot, "logs", "jobs.jsonl");
        Assert.IsTrue(File.Exists(path), $"Expected job trace file at '{path}'.");

        string[] lines = await File.ReadAllLinesAsync(path);
        return lines
            .Select(line => JsonSerializer.Deserialize<JobTraceRecord>(line, JsonOptions)
                ?? throw new InvalidOperationException("Job trace JSON could not be deserialized."))
            .ToArray();
    }

    private static string CreateUniqueTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
