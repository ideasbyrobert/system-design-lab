using Lab.Persistence.Seeding;
using Lab.Persistence.Projections;
using Lab.Persistence.Replication;
using Lab.Persistence.DependencyInjection;
using Lab.Shared.Configuration;
using Lab.Shared.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SeedDataTool.Cli;

if (!SeedDataOptions.TryParse(args, out SeedDataOptions options, out string? error))
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine();
    Console.Error.WriteLine(SeedDataOptions.GetUsage());
    Environment.ExitCode = 1;
    return;
}

if (options.ShowHelp)
{
    Console.WriteLine(SeedDataOptions.GetUsage());
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddLabConfiguration(builder.Configuration, builder.Environment);
builder.Services.AddPrimaryPersistence();
builder.Services.AddReadModelPersistence();
builder.Logging.AddLabOperationalFileLogging();

using var host = builder.Build();
host.LogResolvedLabEnvironment();

ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SeedData");
EnvironmentLayout layout = host.Services.GetRequiredService<EnvironmentLayout>();
RegionOptions regionOptions = host.Services.GetRequiredService<IOptions<RegionOptions>>().Value;
ReplicaSyncOptions replicaSyncOptions = host.Services.GetRequiredService<IOptions<ReplicaSyncOptions>>().Value;
SqliteSeedDataService seeder = host.Services.GetRequiredService<SqliteSeedDataService>();
ProductPageProjectionRebuilder projectionRebuilder = host.Services.GetRequiredService<ProductPageProjectionRebuilder>();
ReplicaSyncService replicaSyncService = host.Services.GetRequiredService<ReplicaSyncService>();

SeedResult? seedResult = null;
ProductPageProjectionRebuildResult? projectionResult = null;
ReplicaSyncBatchResult? replicaSyncResult = null;

if (!options.SkipPrimarySeed)
{
    seedResult = await seeder.SeedAsync(
        layout.PrimaryDatabasePath,
        new SeedCounts(options.ProductCount, options.UserCount),
        options.ResetExisting);

    logger.LogInformation(
        "Seeded primary database {DatabasePath}. Products={ProductsInserted}, Inventory={InventoryInserted}, Users={UsersInserted}.",
        seedResult.DatabasePath,
        seedResult.ProductsInserted,
        seedResult.InventoryRecordsInserted,
        seedResult.UsersInserted);

    Console.WriteLine($"Database: {seedResult.DatabasePath}");
    Console.WriteLine($"Products: {seedResult.ProductsInserted}");
    Console.WriteLine($"Inventory: {seedResult.InventoryRecordsInserted}");
    Console.WriteLine($"Users: {seedResult.UsersInserted}");
}

if (options.RebuildProductPageProjection)
{
    projectionResult = await projectionRebuilder.RebuildAsync(
        layout.PrimaryDatabasePath,
        layout.ReadModelDatabasePath,
        layout.CurrentRegion);

    logger.LogInformation(
        "Rebuilt product-page projection database {DatabasePath}. Region={Region}, RowsWritten={RowsWritten}, ProjectedUtc={ProjectedUtc}.",
        projectionResult.ReadModelDatabasePath,
        projectionResult.Region,
        projectionResult.RowsWritten,
        projectionResult.ProjectedUtc);

    Console.WriteLine($"ReadModel database: {projectionResult.ReadModelDatabasePath}");
    Console.WriteLine($"Projection region: {projectionResult.Region}");
    Console.WriteLine($"ProductPage projections: {projectionResult.RowsWritten}");
}

if (options.SyncReplicas)
{
    ReplicaSyncTarget[] targets =
    [
        new(
            ReplicaRegion: regionOptions.EastReplicaRegion,
            ReplicaDatabasePath: layout.ReplicaEastDatabasePath,
            ConfiguredLag: TimeSpan.FromMilliseconds(options.ReplicaEastLagMillisecondsOverride ?? replicaSyncOptions.EastLagMilliseconds)),
        new(
            ReplicaRegion: regionOptions.WestReplicaRegion,
            ReplicaDatabasePath: layout.ReplicaWestDatabasePath,
            ConfiguredLag: TimeSpan.FromMilliseconds(options.ReplicaWestLagMillisecondsOverride ?? replicaSyncOptions.WestLagMilliseconds))
    ];

    replicaSyncResult = await replicaSyncService.SynchronizeAsync(layout.PrimaryDatabasePath, targets);

    foreach (ReplicaSyncResult replica in replicaSyncResult.Replicas.OrderBy(item => item.ReplicaRegion, StringComparer.Ordinal))
    {
        logger.LogInformation(
            "Replica {ReplicaRegion} synced to {ReplicaDatabasePath}. ObservedLagMs={ObservedLagMs}, Products={Products}, Inventory={Inventory}.",
            replica.ReplicaRegion,
            replica.ReplicaDatabasePath,
            replica.ObservedLagMs,
            replica.ProductCount,
            replica.InventoryRecordCount);

        Console.WriteLine($"Replica region: {replica.ReplicaRegion}");
        Console.WriteLine($"Replica database: {replica.ReplicaDatabasePath}");
        Console.WriteLine($"Replica lag (ms): {replica.ObservedLagMs:0.###}");
        Console.WriteLine($"Replica products: {replica.ProductCount}");
        Console.WriteLine($"Replica inventory: {replica.InventoryRecordCount}");
    }
}
