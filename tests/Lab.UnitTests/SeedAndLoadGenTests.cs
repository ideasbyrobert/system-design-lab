using LoadGenTool.Cli;
using LoadGenTool.Workloads;
using Lab.Persistence;
using Lab.Persistence.Seeding;
using SeedDataTool.Cli;
using Microsoft.Data.Sqlite;

namespace Lab.UnitTests;

[TestClass]
public sealed class SeedAndLoadGenTests
{
    [TestMethod]
    public async Task SqliteSeedDataService_SeedsProductsInventoryAndUsers()
    {
        string root = CreateUniqueTempDirectory();
        string databasePath = Path.Combine(root, "primary.db");
        SqliteSeedDataService seeder = CreateSeeder();

        SeedResult result = await seeder.SeedAsync(
            databasePath,
            new SeedCounts(ProductCount: 3, UserCount: 2),
            resetExisting: true);

        Assert.AreEqual(3, result.ProductsInserted);
        Assert.AreEqual(3, result.InventoryRecordsInserted);
        Assert.AreEqual(2, result.UsersInserted);

        SqliteConnectionStringBuilder builder = new() { DataSource = databasePath };
        await using SqliteConnection connection = new(builder.ToString());
        await connection.OpenAsync();

        Assert.AreEqual(3L, await CountAsync(connection, "products"));
        Assert.AreEqual(3L, await CountAsync(connection, "inventory"));
        Assert.AreEqual(2L, await CountAsync(connection, "users"));
    }

    [TestMethod]
    public void SeedDataOptions_ParseProjectionFlags()
    {
        string[] args =
        [
            "--skip-primary-seed", "true",
            "--rebuild-product-page-projection", "true"
        ];

        bool parsed = SeedDataOptions.TryParse(args, out SeedDataOptions? options, out string? error);

        Assert.IsTrue(parsed, error);
        Assert.IsNotNull(options);
        Assert.IsTrue(options.SkipPrimarySeed);
        Assert.IsTrue(options.RebuildProductPageProjection);
    }

    [TestMethod]
    public void SeedDataOptions_ParseReplicaSyncFlags()
    {
        string[] args =
        [
            "--skip-primary-seed", "true",
            "--sync-replicas", "true",
            "--replica-east-lag-ms", "250",
            "--replica-west-lag-ms", "500"
        ];

        bool parsed = SeedDataOptions.TryParse(args, out SeedDataOptions? options, out string? error);

        Assert.IsTrue(parsed, error);
        Assert.IsNotNull(options);
        Assert.IsTrue(options.SkipPrimarySeed);
        Assert.IsTrue(options.SyncReplicas);
        Assert.AreEqual(250, options.ReplicaEastLagMillisecondsOverride);
        Assert.AreEqual(500, options.ReplicaWestLagMillisecondsOverride);
    }

    [TestMethod]
    public void LoadSchedulePlanner_CreatesConstantAndBurstOffsets()
    {
        LoadGenOptions constant = new(
            TargetUrl: "http://localhost:5000/",
            Method: "GET",
            RequestsPerSecond: 2d,
            Duration: TimeSpan.FromSeconds(2),
            ConcurrencyCap: 2,
            Headers: new Dictionary<string, string>(),
            PayloadFile: null,
            RunId: "run-constant",
            Mode: WorkloadMode.Constant,
            BurstSize: null,
            BurstPeriod: TimeSpan.FromSeconds(1),
            ShowHelp: false);

        LoadGenOptions burst = constant with
        {
            Mode = WorkloadMode.Burst,
            BurstSize = 3,
            Duration = TimeSpan.FromMilliseconds(2500)
        };

        IReadOnlyList<TimeSpan> constantOffsets = LoadSchedulePlanner.CreateOffsets(constant);
        IReadOnlyList<TimeSpan> burstOffsets = LoadSchedulePlanner.CreateOffsets(burst);

        CollectionAssert.AreEqual(
            new[]
            {
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromMilliseconds(1000),
                TimeSpan.FromMilliseconds(1500)
            },
            constantOffsets.ToArray());

        CollectionAssert.AreEqual(
            new[]
            {
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(2)
            },
            burstOffsets.ToArray());
    }

    private static async Task<long> CountAsync(SqliteConnection connection, string tableName)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        object? scalar = await command.ExecuteScalarAsync();
        return Convert.ToInt64(scalar, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static SqliteSeedDataService CreateSeeder()
    {
        PrimaryDbContextFactory dbContextFactory = new();
        PrimaryDatabaseInitializer initializer = new(dbContextFactory);
        return new SqliteSeedDataService(initializer, dbContextFactory);
    }

    private static string CreateUniqueTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
