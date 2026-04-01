using Lab.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lab.Persistence.Seeding;

public sealed class SqliteSeedDataService(
    PrimaryDatabaseInitializer databaseInitializer,
    PrimaryDbContextFactory dbContextFactory)
{
    private static readonly string[] Categories = ["apparel", "books", "electronics", "home", "kitchen"];
    private static readonly string[] Regions = ["local", "east", "west"];
    private static readonly DateTimeOffset SeedEpochUtc = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public async Task<SeedResult> SeedAsync(
        string databasePath,
        SeedCounts counts,
        bool resetExisting = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(counts);

        string fullPath = Path.GetFullPath(databasePath);
        string? directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await databaseInitializer.InitializeAsync(fullPath, cancellationToken);
        await using PrimaryDbContext dbContext = dbContextFactory.CreateDbContext(fullPath);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (resetExisting)
        {
            await ResetExistingRowsAsync(dbContext, cancellationToken);
        }

        IReadOnlyList<Product> products = CreateProducts(counts.ProductCount);
        IReadOnlyList<InventoryRecord> inventoryRecords = CreateInventoryRecords(products);
        IReadOnlyList<User> users = CreateUsers(counts.UserCount);

        await dbContext.Products.AddRangeAsync(products, cancellationToken);
        await dbContext.Inventory.AddRangeAsync(inventoryRecords, cancellationToken);
        await dbContext.Users.AddRangeAsync(users, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new SeedResult(fullPath, products.Count, inventoryRecords.Count, users.Count);
    }

    private static async Task ResetExistingRowsAsync(PrimaryDbContext dbContext, CancellationToken cancellationToken)
    {
        await dbContext.Payments.ExecuteDeleteAsync(cancellationToken);
        await dbContext.OrderItems.ExecuteDeleteAsync(cancellationToken);
        await dbContext.CartItems.ExecuteDeleteAsync(cancellationToken);
        await dbContext.QueueJobs.ExecuteDeleteAsync(cancellationToken);
        await dbContext.Orders.ExecuteDeleteAsync(cancellationToken);
        await dbContext.Carts.ExecuteDeleteAsync(cancellationToken);
        await dbContext.Inventory.ExecuteDeleteAsync(cancellationToken);
        await dbContext.Products.ExecuteDeleteAsync(cancellationToken);
        await dbContext.Users.ExecuteDeleteAsync(cancellationToken);
    }

    private static IReadOnlyList<Product> CreateProducts(int productCount)
    {
        List<Product> products = new(productCount);

        for (int index = 1; index <= productCount; index++)
        {
            DateTimeOffset timestamp = SeedEpochUtc.AddMinutes(index - 1);
            products.Add(new Product
            {
                ProductId = $"sku-{index:D4}",
                Name = $"Sample Product {index:D4}",
                Description = $"Deterministic sample product {index:D4} for lab experiments.",
                PriceCents = 999 + (index * 137),
                Category = Categories[(index - 1) % Categories.Length],
                Version = 1,
                CreatedUtc = timestamp,
                UpdatedUtc = timestamp
            });
        }

        return products;
    }

    private static IReadOnlyList<InventoryRecord> CreateInventoryRecords(IReadOnlyList<Product> products)
    {
        List<InventoryRecord> inventoryRecords = new(products.Count);

        for (int index = 0; index < products.Count; index++)
        {
            Product product = products[index];
            inventoryRecords.Add(new InventoryRecord
            {
                ProductId = product.ProductId,
                AvailableQuantity = 100 + ((index + 1) % 25),
                ReservedQuantity = (index + 1) % 5,
                Version = 1,
                UpdatedUtc = product.UpdatedUtc
            });
        }

        return inventoryRecords;
    }

    private static IReadOnlyList<User> CreateUsers(int userCount)
    {
        List<User> users = new(userCount);

        for (int index = 1; index <= userCount; index++)
        {
            users.Add(new User
            {
                UserId = $"user-{index:D4}",
                Email = $"user{index:D4}@example.test",
                DisplayName = $"Sample User {index:D4}",
                Region = Regions[(index - 1) % Regions.Length],
                CreatedUtc = SeedEpochUtc.AddHours(index)
            });
        }

        return users;
    }
}
