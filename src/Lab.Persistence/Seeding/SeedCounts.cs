namespace Lab.Persistence.Seeding;

public sealed record SeedCounts(int ProductCount, int UserCount)
{
    public int InventoryCount => ProductCount;

    public static SeedCounts Default { get; } = new(ProductCount: 50, UserCount: 10);
}
