namespace Lab.Persistence.Seeding;

public sealed record SeedResult(
    string DatabasePath,
    int ProductsInserted,
    int InventoryRecordsInserted,
    int UsersInserted);
