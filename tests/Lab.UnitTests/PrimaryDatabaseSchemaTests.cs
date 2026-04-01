using System.Data.Common;
using Lab.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lab.UnitTests;

[TestClass]
public sealed class PrimaryDatabaseSchemaTests
{
    [TestMethod]
    public async Task PrimaryDatabaseInitializer_CreatesAllExpectedTables()
    {
        string root = CreateUniqueTempDirectory();
        string databasePath = Path.Combine(root, "primary.db");
        PrimaryDbContextFactory dbContextFactory = new();
        PrimaryDatabaseInitializer initializer = new(dbContextFactory);

        await initializer.InitializeAsync(databasePath);

        await using PrimaryDbContext dbContext = dbContextFactory.CreateDbContext(databasePath);
        await dbContext.Database.OpenConnectionAsync();

        DbConnection connection = dbContext.Database.GetDbConnection();

        CollectionAssert.AreEquivalent(
            new[]
            {
                "__EFMigrationsHistory",
                "__EFMigrationsLock",
                "cart_items",
                "carts",
                "inventory",
                "order_items",
                "orders",
                "payments",
                "products",
                "queue_jobs",
                "users"
            },
            (await GetTableNamesAsync(connection)).ToArray());

        Assert.AreEqual("wal", await GetScalarStringAsync(connection, "PRAGMA journal_mode;"));
        Assert.AreEqual("5000", await GetScalarStringAsync(connection, "PRAGMA busy_timeout;"));
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

    private static string CreateUniqueTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
