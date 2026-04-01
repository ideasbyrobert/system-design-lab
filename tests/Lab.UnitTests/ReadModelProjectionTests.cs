using System.Data.Common;
using System.Text.Json;
using Lab.Persistence;
using Lab.Persistence.Entities;
using Lab.Persistence.Projections;
using Lab.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace Lab.UnitTests;

[TestClass]
public sealed class ReadModelProjectionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task ReadModelDatabaseInitializer_CreatesProjectionTables()
    {
        string root = CreateUniqueTempDirectory();
        string databasePath = Path.Combine(root, "readmodels.db");
        ReadModelDbContextFactory dbContextFactory = new();
        ReadModelDatabaseInitializer initializer = new(dbContextFactory);

        await initializer.InitializeAsync(databasePath);

        await using ReadModelDbContext dbContext = dbContextFactory.CreateDbContext(databasePath);
        await dbContext.Database.OpenConnectionAsync();

        DbConnection connection = dbContext.Database.GetDbConnection();

        CollectionAssert.AreEquivalent(
            new[]
            {
                "ReadModel_OrderHistory",
                "ReadModel_ProductPage"
            },
            (await GetTableNamesAsync(connection)).ToArray());

        Assert.AreEqual("wal", await GetScalarStringAsync(connection, "PRAGMA journal_mode;"));
        Assert.AreEqual("5000", await GetScalarStringAsync(connection, "PRAGMA busy_timeout;"));
    }

    [TestMethod]
    public async Task ProductPageProjectionRebuilder_BuildsRepeatableProjectionRowsFromPrimaryTables()
    {
        string root = CreateUniqueTempDirectory();
        string primaryDatabasePath = Path.Combine(root, "primary.db");
        string readModelDatabasePath = Path.Combine(root, "readmodels.db");

        SqliteSeedDataService seeder = CreateSeeder();
        SeedResult seedResult = await seeder.SeedAsync(
            primaryDatabasePath,
            new SeedCounts(ProductCount: 3, UserCount: 2),
            resetExisting: true);

        Assert.AreEqual(3, seedResult.ProductsInserted);

        ManualTimeProvider timeProvider = new(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));
        ProductPageProjectionRebuilder rebuilder = CreateRebuilder(timeProvider);

        ProductPageProjectionRebuildResult first = await rebuilder.RebuildAsync(
            primaryDatabasePath,
            readModelDatabasePath,
            region: "local");

        Assert.AreEqual(readModelDatabasePath, first.ReadModelDatabasePath);
        Assert.AreEqual("local", first.Region);
        Assert.AreEqual(3, first.RowsWritten);
        Assert.IsTrue(File.Exists(readModelDatabasePath));

        ReadModelDbContextFactory readModelFactory = new();
        await using ReadModelDbContext readModelDbContext = readModelFactory.CreateDbContext(readModelDatabasePath);

        List<Lab.Persistence.Entities.ReadModelProductPage> firstRows = await readModelDbContext.ProductPages
            .AsNoTracking()
            .OrderBy(row => row.ProductId)
            .ToListAsync();

        Assert.HasCount(3, firstRows);
        Lab.Persistence.Entities.ReadModelProductPage firstSku = firstRows[0];
        ProductPageSummaryContract firstSummary = JsonSerializer.Deserialize<ProductPageSummaryContract>(firstSku.SummaryJson, JsonOptions)
            ?? throw new InvalidOperationException("Projection summary JSON could not be deserialized.");

        Assert.AreEqual("sku-0001", firstSku.ProductId);
        Assert.AreEqual("local", firstSku.Region);
        Assert.AreEqual(1L, firstSku.ProjectionVersion);
        Assert.AreEqual("Sample Product 0001", firstSummary.Name);
        Assert.AreEqual("apparel", firstSummary.Category);
        Assert.AreEqual(1136, firstSummary.Price.AmountCents);
        Assert.AreEqual("$11.36", firstSummary.Price.Display);
        Assert.AreEqual(101, firstSummary.Inventory.AvailableQuantity);
        Assert.AreEqual(1, firstSummary.Inventory.ReservedQuantity);
        Assert.AreEqual(100, firstSummary.Inventory.SellableQuantity);
        Assert.AreEqual("in_stock", firstSummary.Inventory.StockStatus);
        Assert.AreEqual(1L, firstSummary.Versions.ProductVersion);
        Assert.AreEqual(1L, firstSummary.Versions.InventoryVersion);
        Assert.AreEqual(1L, firstSummary.Versions.ProjectionVersion);

        timeProvider.Advance(TimeSpan.FromMinutes(5));

        ProductPageProjectionRebuildResult second = await rebuilder.RebuildAsync(
            primaryDatabasePath,
            readModelDatabasePath,
            region: "local");

        List<Lab.Persistence.Entities.ReadModelProductPage> secondRows = await readModelDbContext.ProductPages
            .AsNoTracking()
            .OrderBy(row => row.ProductId)
            .ToListAsync();

        Assert.AreEqual(3, second.RowsWritten);
        Assert.HasCount(3, secondRows);
        Assert.AreEqual(firstSku.SummaryJson, secondRows[0].SummaryJson);
        Assert.AreEqual(new DateTimeOffset(2026, 4, 1, 0, 5, 0, TimeSpan.Zero), secondRows[0].ProjectedUtc);
    }

    [TestMethod]
    public async Task OrderHistoryProjectionRebuilder_BuildsRepeatableProjectionRowsFromPrimaryTables()
    {
        string root = CreateUniqueTempDirectory();
        string primaryDatabasePath = Path.Combine(root, "primary.db");
        string readModelDatabasePath = Path.Combine(root, "readmodels.db");

        SqliteSeedDataService seeder = CreateSeeder();
        SeedResult seedResult = await seeder.SeedAsync(
            primaryDatabasePath,
            new SeedCounts(ProductCount: 3, UserCount: 2),
            resetExisting: true);

        Assert.AreEqual(3, seedResult.ProductsInserted);

        await using (PrimaryDbContext primaryDbContext = new PrimaryDbContextFactory().CreateDbContext(primaryDatabasePath))
        {
            primaryDbContext.Orders.Add(new Order
            {
                OrderId = "order-rm-001",
                UserId = "user-0001",
                CartId = "cart-rm-001",
                Region = "local",
                Status = "Paid",
                TotalPriceCents = 2409,
                CreatedUtc = new DateTimeOffset(2026, 4, 1, 1, 0, 0, TimeSpan.Zero),
                SubmittedUtc = new DateTimeOffset(2026, 4, 1, 1, 1, 0, TimeSpan.Zero)
            });
            primaryDbContext.OrderItems.AddRange(
                new OrderItem
                {
                    OrderItemId = "oi-rm-001",
                    OrderId = "order-rm-001",
                    ProductId = "sku-0001",
                    Quantity = 1,
                    UnitPriceCents = 1136
                },
                new OrderItem
                {
                    OrderItemId = "oi-rm-002",
                    OrderId = "order-rm-001",
                    ProductId = "sku-0002",
                    Quantity = 1,
                    UnitPriceCents = 1273
                });
            primaryDbContext.Payments.Add(new Payment
            {
                PaymentId = "payment-rm-001",
                OrderId = "order-rm-001",
                Provider = "PaymentSimulator",
                IdempotencyKey = "idem-rm-001",
                Mode = "fast_success",
                Status = "Authorized",
                AmountCents = 2409,
                ExternalReference = "psim-ref-rm-001",
                AttemptedUtc = new DateTimeOffset(2026, 4, 1, 1, 1, 30, TimeSpan.Zero),
                ConfirmedUtc = new DateTimeOffset(2026, 4, 1, 1, 1, 31, TimeSpan.Zero)
            });

            await primaryDbContext.SaveChangesAsync();
        }

        ManualTimeProvider timeProvider = new(new DateTimeOffset(2026, 4, 1, 2, 0, 0, TimeSpan.Zero));
        OrderHistoryProjectionRebuilder rebuilder = CreateOrderHistoryRebuilder(timeProvider);

        OrderHistoryProjectionRebuildResult first = await rebuilder.RebuildAsync(
            primaryDatabasePath,
            readModelDatabasePath,
            userId: "user-0001");

        Assert.AreEqual(readModelDatabasePath, first.ReadModelDatabasePath);
        Assert.AreEqual("user-0001", first.UserId);
        Assert.AreEqual(1, first.RowsWritten);
        Assert.IsTrue(first.ProjectionRowWritten);

        ReadModelDbContextFactory readModelFactory = new();
        await using ReadModelDbContext readModelDbContext = readModelFactory.CreateDbContext(readModelDatabasePath);

        List<ReadModelOrderHistory> firstRows = await readModelDbContext.OrderHistories
            .AsNoTracking()
            .OrderBy(row => row.OrderId)
            .ToListAsync();

        Assert.HasCount(1, firstRows);
        ReadModelOrderHistory firstRow = firstRows[0];
        OrderHistoryProjectionSummary firstSummary = JsonSerializer.Deserialize<OrderHistoryProjectionSummary>(firstRow.SummaryJson, JsonOptions)
            ?? throw new InvalidOperationException("Order-history projection summary JSON could not be deserialized.");

        Assert.AreEqual("order-rm-001", firstRow.OrderId);
        Assert.AreEqual("user-0001", firstRow.UserId);
        Assert.AreEqual("Paid", firstRow.Status);
        Assert.AreEqual("local", firstRow.Region);
        Assert.AreEqual(2, firstSummary.ItemCount);
        Assert.AreEqual(2409, firstSummary.TotalAmountCents);
        Assert.AreEqual("Authorized", firstSummary.Payment?.Status);
        Assert.AreEqual("psim-ref-rm-001", firstSummary.Payment?.ProviderReference);
        Assert.AreEqual("Sample Product 0001", firstSummary.Items[0].ProductName);
        Assert.AreEqual(1136, firstSummary.Items[0].LineSubtotalCents);
        Assert.AreEqual(firstRow.ProjectionVersion, firstSummary.Versions.ProjectionVersion);

        timeProvider.Advance(TimeSpan.FromMinutes(5));

        OrderHistoryProjectionRebuildResult second = await rebuilder.RebuildAsync(
            primaryDatabasePath,
            readModelDatabasePath,
            userId: "user-0001");

        List<ReadModelOrderHistory> secondRows = await readModelDbContext.OrderHistories
            .AsNoTracking()
            .OrderBy(row => row.OrderId)
            .ToListAsync();

        Assert.AreEqual(1, second.RowsWritten);
        Assert.HasCount(1, secondRows);
        Assert.AreEqual(firstRow.SummaryJson, secondRows[0].SummaryJson);
        Assert.AreEqual(new DateTimeOffset(2026, 4, 1, 2, 5, 0, TimeSpan.Zero), secondRows[0].ProjectedUtc);
    }

    private static async Task<IReadOnlyList<string>> GetTableNamesAsync(DbConnection connection)
    {
        List<string> names = [];

        await using DbCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
              AND name NOT LIKE 'sqlite_%'
            ORDER BY name;
            """;

        await using DbDataReader reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task<string> GetScalarStringAsync(DbConnection connection, string sql)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? scalar = await command.ExecuteScalarAsync();
        return Convert.ToString(scalar, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static SqliteSeedDataService CreateSeeder()
    {
        PrimaryDbContextFactory dbContextFactory = new();
        PrimaryDatabaseInitializer initializer = new(dbContextFactory);
        return new SqliteSeedDataService(initializer, dbContextFactory);
    }

    private static ProductPageProjectionRebuilder CreateRebuilder(TimeProvider timeProvider)
    {
        PrimaryDbContextFactory primaryDbContextFactory = new();
        ReadModelDbContextFactory readModelDbContextFactory = new();

        return new ProductPageProjectionRebuilder(
            new PrimaryDatabaseInitializer(primaryDbContextFactory),
            primaryDbContextFactory,
            new ReadModelDatabaseInitializer(readModelDbContextFactory),
            readModelDbContextFactory,
            timeProvider);
    }

    private static OrderHistoryProjectionRebuilder CreateOrderHistoryRebuilder(TimeProvider timeProvider)
    {
        PrimaryDbContextFactory primaryDbContextFactory = new();
        ReadModelDbContextFactory readModelDbContextFactory = new();

        return new OrderHistoryProjectionRebuilder(
            new PrimaryDatabaseInitializer(primaryDbContextFactory),
            primaryDbContextFactory,
            new ReadModelDatabaseInitializer(readModelDbContextFactory),
            readModelDbContextFactory,
            timeProvider);
    }

    private static string CreateUniqueTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed record ProductPagePriceContract(int AmountCents, string CurrencyCode, string Display);

    private sealed record ProductPageInventoryContract(
        int AvailableQuantity,
        int ReservedQuantity,
        int SellableQuantity,
        string StockStatus);

    private sealed record ProductPageVersionsContract(long ProductVersion, long InventoryVersion, long ProjectionVersion);

    private sealed record ProductPageSummaryContract(
        string ProductId,
        string Name,
        string Description,
        string Category,
        ProductPagePriceContract Price,
        ProductPageInventoryContract Inventory,
        ProductPageVersionsContract Versions);

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan delta) => utcNow = utcNow.Add(delta);
    }
}
