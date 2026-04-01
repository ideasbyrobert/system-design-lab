using Lab.Persistence;
using Lab.Persistence.Entities;
using Lab.Persistence.Replication;
using Lab.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lab.UnitTests;

[TestClass]
public sealed class ReplicaSyncServiceTests
{
    [TestMethod]
    public async Task ReplicaSyncService_CopiesProductsAndInventoryIntoReplica()
    {
        string root = CreateUniqueTempDirectory();
        string primaryDatabasePath = Path.Combine(root, "primary.db");
        string replicaDatabasePath = Path.Combine(root, "replica-east.db");

        SqliteSeedDataService seeder = CreateSeeder();
        await seeder.SeedAsync(
            primaryDatabasePath,
            new SeedCounts(ProductCount: 3, UserCount: 2),
            resetExisting: true);

        ReplicaSyncService service = CreateReplicaSyncService();
        ReplicaSyncBatchResult result = await service.SynchronizeAsync(
            primaryDatabasePath,
            [new ReplicaSyncTarget("east", replicaDatabasePath, TimeSpan.Zero)]);

        ReplicaSyncResult replica = result.Replicas.Single();

        Assert.AreEqual(ReplicaSyncService.MechanismName, replica.Mechanism);
        Assert.AreEqual(3, replica.ProductCount);
        Assert.AreEqual(3, replica.InventoryRecordCount);
        Assert.IsGreaterThanOrEqualTo(0d, replica.ObservedLagMs);

        PrimaryDbContextFactory factory = new();
        await using PrimaryDbContext dbContext = factory.CreateDbContext(replicaDatabasePath);

        Assert.AreEqual(3, await dbContext.Products.CountAsync());
        Assert.AreEqual(3, await dbContext.Inventory.CountAsync());
        Assert.AreEqual(0, await dbContext.Users.CountAsync());

        Product product = await dbContext.Products.SingleAsync(item => item.ProductId == "sku-0001");
        InventoryRecord inventory = await dbContext.Inventory.SingleAsync(item => item.ProductId == "sku-0001");

        Assert.AreEqual("Sample Product 0001", product.Name);
        Assert.AreEqual(1136, product.PriceCents);
        Assert.AreEqual(101, inventory.AvailableQuantity);
        Assert.AreEqual(1, inventory.ReservedQuantity);
    }

    [TestMethod]
    public async Task ReplicaSyncService_UsesLaggedSnapshotRatherThanLatestPrimaryState()
    {
        string root = CreateUniqueTempDirectory();
        string primaryDatabasePath = Path.Combine(root, "primary.db");
        string replicaDatabasePath = Path.Combine(root, "replica-east.db");

        SqliteSeedDataService seeder = CreateSeeder();
        await seeder.SeedAsync(
            primaryDatabasePath,
            new SeedCounts(ProductCount: 1, UserCount: 1),
            resetExisting: true);

        PrimaryDbContextFactory factory = new();
        ReplicaSyncService service = CreateReplicaSyncService();

        Task<ReplicaSyncBatchResult> syncTask = service.SynchronizeAsync(
            primaryDatabasePath,
            [new ReplicaSyncTarget("east", replicaDatabasePath, TimeSpan.FromMilliseconds(150))]);

        await Task.Delay(40);

        await using (PrimaryDbContext primaryDbContext = factory.CreateDbContext(primaryDatabasePath))
        {
            Product product = await primaryDbContext.Products.SingleAsync(item => item.ProductId == "sku-0001");
            InventoryRecord inventory = await primaryDbContext.Inventory.SingleAsync(item => item.ProductId == "sku-0001");

            product.PriceCents = 7777;
            product.Version = 2;
            product.UpdatedUtc = product.UpdatedUtc.AddMinutes(5);
            inventory.AvailableQuantity = 42;
            inventory.Version = 2;
            inventory.UpdatedUtc = inventory.UpdatedUtc.AddMinutes(5);

            await primaryDbContext.SaveChangesAsync();
        }

        ReplicaSyncResult staleReplica = (await syncTask).Replicas.Single();

        Assert.IsGreaterThanOrEqualTo(120d, staleReplica.ObservedLagMs);

        await using (PrimaryDbContext replicaDbContext = factory.CreateDbContext(replicaDatabasePath))
        {
            Product product = await replicaDbContext.Products.SingleAsync(item => item.ProductId == "sku-0001");
            InventoryRecord inventory = await replicaDbContext.Inventory.SingleAsync(item => item.ProductId == "sku-0001");

            Assert.AreEqual(1136, product.PriceCents);
            Assert.AreEqual(1L, product.Version);
            Assert.AreEqual(101, inventory.AvailableQuantity);
            Assert.AreEqual(1L, inventory.Version);
        }

        await service.SynchronizeAsync(
            primaryDatabasePath,
            [new ReplicaSyncTarget("east", replicaDatabasePath, TimeSpan.Zero)]);

        await using (PrimaryDbContext refreshedReplicaDbContext = factory.CreateDbContext(replicaDatabasePath))
        {
            Product product = await refreshedReplicaDbContext.Products.SingleAsync(item => item.ProductId == "sku-0001");
            InventoryRecord inventory = await refreshedReplicaDbContext.Inventory.SingleAsync(item => item.ProductId == "sku-0001");

            Assert.AreEqual(7777, product.PriceCents);
            Assert.AreEqual(2L, product.Version);
            Assert.AreEqual(42, inventory.AvailableQuantity);
            Assert.AreEqual(2L, inventory.Version);
        }
    }

    private static SqliteSeedDataService CreateSeeder()
    {
        PrimaryDbContextFactory dbContextFactory = new();
        PrimaryDatabaseInitializer initializer = new(dbContextFactory);
        return new SqliteSeedDataService(initializer, dbContextFactory);
    }

    private static ReplicaSyncService CreateReplicaSyncService()
    {
        PrimaryDbContextFactory dbContextFactory = new();
        PrimaryDatabaseInitializer initializer = new(dbContextFactory);
        return new ReplicaSyncService(
            dbContextFactory,
            initializer,
            TimeProvider.System,
            NullLogger<ReplicaSyncService>.Instance);
    }

    private static string CreateUniqueTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
