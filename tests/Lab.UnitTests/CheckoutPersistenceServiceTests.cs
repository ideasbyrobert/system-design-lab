using Lab.Persistence;
using Lab.Persistence.Checkout;
using Lab.Persistence.Entities;
using Lab.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace Lab.UnitTests;

[TestClass]
public sealed class CheckoutPersistenceServiceTests
{
    [TestMethod]
    public async Task ReserveInventoryAsync_DecrementsAvailableAndIncrementsReserved()
    {
        string databasePath = await InitializeAndSeedAsync(productCount: 3, userCount: 1);

        await using PrimaryDbContext beforeContext = CreateDbContext(databasePath);
        InventoryRecord beforeInventory = await beforeContext.Inventory
            .AsNoTracking()
            .SingleAsync(item => item.ProductId == "sku-0001");

        await using PrimaryDbContext dbContext = CreateDbContext(databasePath);
        CheckoutPersistenceService service = new(dbContext);
        DateTimeOffset updatedUtc = new(2026, 3, 31, 15, 0, 0, TimeSpan.Zero);

        InventoryReservationResult result = await service.ReserveInventoryAsync(
            [new InventoryReservationRequest("sku-0001", 3)],
            updatedUtc);

        Assert.IsTrue(result.Succeeded);
        Assert.HasCount(1, result.Inventory);
        Assert.AreEqual("sku-0001", result.Inventory[0].ProductId);
        Assert.AreEqual(beforeInventory.AvailableQuantity - 3, result.Inventory[0].AvailableQuantity);
        Assert.AreEqual(beforeInventory.ReservedQuantity + 3, result.Inventory[0].ReservedQuantity);
        Assert.AreEqual(beforeInventory.Version + 1, result.Inventory[0].Version);
        Assert.AreEqual(updatedUtc, result.Inventory[0].UpdatedUtc);

        await using PrimaryDbContext afterContext = CreateDbContext(databasePath);
        InventoryRecord afterInventory = await afterContext.Inventory
            .AsNoTracking()
            .SingleAsync(item => item.ProductId == "sku-0001");

        Assert.AreEqual(beforeInventory.AvailableQuantity - 3, afterInventory.AvailableQuantity);
        Assert.AreEqual(beforeInventory.ReservedQuantity + 3, afterInventory.ReservedQuantity);
    }

    [TestMethod]
    public async Task ReserveInventoryAndPersistOrderAsync_PersistsOrderItemsAndPaymentTogether()
    {
        string databasePath = await InitializeAndSeedAsync(productCount: 4, userCount: 2);

        await using PrimaryDbContext seedContext = CreateDbContext(databasePath);
        Product sku1 = await seedContext.Products.AsNoTracking().SingleAsync(item => item.ProductId == "sku-0001");
        Product sku2 = await seedContext.Products.AsNoTracking().SingleAsync(item => item.ProductId == "sku-0002");
        InventoryRecord sku1InventoryBefore = await seedContext.Inventory.AsNoTracking().SingleAsync(item => item.ProductId == "sku-0001");
        InventoryRecord sku2InventoryBefore = await seedContext.Inventory.AsNoTracking().SingleAsync(item => item.ProductId == "sku-0002");

        int totalPriceCents = (2 * sku1.PriceCents) + sku2.PriceCents;

        await using PrimaryDbContext dbContext = CreateDbContext(databasePath);
        CheckoutPersistenceService service = new(dbContext);
        DateTimeOffset createdUtc = new(2026, 3, 31, 16, 0, 0, TimeSpan.Zero);

        CheckoutPersistenceResult result = await service.ReserveInventoryAndPersistOrderAsync(
            new CheckoutPersistenceRequest(
                OrderId: "order-040-001",
                UserId: "user-0001",
                CartId: "cart-040-001",
                Region: "local",
                OrderStatus: "PendingPayment",
                CreatedUtc: createdUtc,
                SubmittedUtc: createdUtc,
                Items:
                [
                    new CheckoutOrderItemPersistenceRequest("sku-0001", 2, sku1.PriceCents),
                    new CheckoutOrderItemPersistenceRequest("sku-0002", 1, sku2.PriceCents)
                ],
                Payment: new CheckoutPaymentPersistenceRequest(
                    PaymentId: "payment-040-001",
                    Provider: "PaymentSimulator",
                    IdempotencyKey: "idem-040-001",
                    Mode: "fast_success",
                    Status: "pending",
                    AmountCents: totalPriceCents,
                    ExternalReference: "ext-040-001",
                    ErrorCode: null,
                    AttemptedUtc: createdUtc,
                    ConfirmedUtc: null)));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("order-040-001", result.OrderId);
        Assert.AreEqual("payment-040-001", result.PaymentId);
        Assert.AreEqual(totalPriceCents, result.TotalPriceCents);
        Assert.HasCount(2, result.Inventory);

        await using PrimaryDbContext verificationContext = CreateDbContext(databasePath);
        Order order = await verificationContext.Orders
            .Include(item => item.Items)
            .Include(item => item.Payments)
            .SingleAsync(item => item.OrderId == "order-040-001");

        Assert.AreEqual("user-0001", order.UserId);
        Assert.AreEqual("cart-040-001", order.CartId);
        Assert.AreEqual("PendingPayment", order.Status);
        Assert.AreEqual(totalPriceCents, order.TotalPriceCents);
        Assert.HasCount(2, order.Items);
        Assert.HasCount(1, order.Payments);
        Assert.AreEqual("pending", order.Payments.Single().Status);
        Assert.AreEqual(totalPriceCents, order.Payments.Single().AmountCents);
        Assert.AreEqual("idem-040-001", order.Payments.Single().IdempotencyKey);
        Assert.AreEqual("fast_success", order.Payments.Single().Mode);

        InventoryRecord sku1InventoryAfter = await verificationContext.Inventory.AsNoTracking().SingleAsync(item => item.ProductId == "sku-0001");
        InventoryRecord sku2InventoryAfter = await verificationContext.Inventory.AsNoTracking().SingleAsync(item => item.ProductId == "sku-0002");

        Assert.AreEqual(sku1InventoryBefore.AvailableQuantity - 2, sku1InventoryAfter.AvailableQuantity);
        Assert.AreEqual(sku1InventoryBefore.ReservedQuantity + 2, sku1InventoryAfter.ReservedQuantity);
        Assert.AreEqual(sku2InventoryBefore.AvailableQuantity - 1, sku2InventoryAfter.AvailableQuantity);
        Assert.AreEqual(sku2InventoryBefore.ReservedQuantity + 1, sku2InventoryAfter.ReservedQuantity);
    }

    [TestMethod]
    public async Task ReserveInventoryAndPersistOrderAsync_RollsBackAllChangesWhenAnyReservationFails()
    {
        string databasePath = await InitializeAndSeedAsync(productCount: 3, userCount: 1);

        await using PrimaryDbContext seedContext = CreateDbContext(databasePath);
        Product sku1 = await seedContext.Products.AsNoTracking().SingleAsync(item => item.ProductId == "sku-0001");
        Product sku2 = await seedContext.Products.AsNoTracking().SingleAsync(item => item.ProductId == "sku-0002");
        InventoryRecord sku1InventoryBefore = await seedContext.Inventory.AsNoTracking().SingleAsync(item => item.ProductId == "sku-0001");
        InventoryRecord sku2InventoryBefore = await seedContext.Inventory.AsNoTracking().SingleAsync(item => item.ProductId == "sku-0002");

        await using PrimaryDbContext dbContext = CreateDbContext(databasePath);
        CheckoutPersistenceService service = new(dbContext);
        DateTimeOffset createdUtc = new(2026, 3, 31, 17, 0, 0, TimeSpan.Zero);

        CheckoutPersistenceResult result = await service.ReserveInventoryAndPersistOrderAsync(
            new CheckoutPersistenceRequest(
                OrderId: "order-040-rollback",
                UserId: "user-0001",
                CartId: null,
                Region: "local",
                OrderStatus: "PendingPayment",
                CreatedUtc: createdUtc,
                SubmittedUtc: createdUtc,
                Items:
                [
                    new CheckoutOrderItemPersistenceRequest("sku-0001", 2, sku1.PriceCents),
                    new CheckoutOrderItemPersistenceRequest("sku-0002", sku2InventoryBefore.AvailableQuantity + 100, sku2.PriceCents)
                ],
                Payment: new CheckoutPaymentPersistenceRequest(
                    PaymentId: "payment-040-rollback",
                    Provider: "PaymentSimulator",
                    IdempotencyKey: "idem-040-rollback",
                    Mode: "fast_success",
                    Status: "pending",
                    AmountCents: (2 * sku1.PriceCents) + ((sku2InventoryBefore.AvailableQuantity + 100) * sku2.PriceCents),
                    ExternalReference: null,
                    ErrorCode: null,
                    AttemptedUtc: createdUtc,
                    ConfirmedUtc: null)));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("insufficient_inventory", result.Failure?.Code);
        Assert.AreEqual("sku-0002", result.Failure?.ProductId);

        await using PrimaryDbContext verificationContext = CreateDbContext(databasePath);
        InventoryRecord sku1InventoryAfter = await verificationContext.Inventory.AsNoTracking().SingleAsync(item => item.ProductId == "sku-0001");
        InventoryRecord sku2InventoryAfter = await verificationContext.Inventory.AsNoTracking().SingleAsync(item => item.ProductId == "sku-0002");

        Assert.AreEqual(sku1InventoryBefore.AvailableQuantity, sku1InventoryAfter.AvailableQuantity);
        Assert.AreEqual(sku1InventoryBefore.ReservedQuantity, sku1InventoryAfter.ReservedQuantity);
        Assert.AreEqual(sku2InventoryBefore.AvailableQuantity, sku2InventoryAfter.AvailableQuantity);
        Assert.AreEqual(sku2InventoryBefore.ReservedQuantity, sku2InventoryAfter.ReservedQuantity);
        Assert.AreEqual(0, await verificationContext.Orders.CountAsync());
        Assert.AreEqual(0, await verificationContext.OrderItems.CountAsync());
        Assert.AreEqual(0, await verificationContext.Payments.CountAsync());
    }

    [TestMethod]
    public async Task InventoryCheckConstraints_PreventNegativeInventory()
    {
        string databasePath = await InitializeAndSeedAsync(productCount: 2, userCount: 1);

        await using PrimaryDbContext dbContext = CreateDbContext(databasePath);
        InventoryRecord inventory = await dbContext.Inventory.SingleAsync(item => item.ProductId == "sku-0001");
        inventory.AvailableQuantity = -1;

        bool threw = false;

        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "Expected the database to reject negative inventory.");

        await using PrimaryDbContext verificationContext = CreateDbContext(databasePath);
        InventoryRecord inventoryAfter = await verificationContext.Inventory.AsNoTracking().SingleAsync(item => item.ProductId == "sku-0001");
        Assert.IsGreaterThanOrEqualTo(0, inventoryAfter.AvailableQuantity);
    }

    private static async Task<string> InitializeAndSeedAsync(int productCount, int userCount)
    {
        string root = CreateUniqueTempDirectory();
        string databasePath = Path.Combine(root, "primary.db");
        PrimaryDbContextFactory dbContextFactory = new();
        PrimaryDatabaseInitializer initializer = new(dbContextFactory);
        SqliteSeedDataService seeder = new(initializer, dbContextFactory);

        await seeder.SeedAsync(databasePath, new SeedCounts(productCount, userCount), resetExisting: true);
        return databasePath;
    }

    private static PrimaryDbContext CreateDbContext(string databasePath)
    {
        PrimaryDbContextFactory factory = new();
        return factory.CreateDbContext(databasePath);
    }

    private static string CreateUniqueTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
