using System.Data;
using System.Diagnostics;
using Lab.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Lab.Persistence.Replication;

public sealed class ReplicaSyncService(
    PrimaryDbContextFactory dbContextFactory,
    PrimaryDatabaseInitializer databaseInitializer,
    TimeProvider timeProvider,
    ILogger<ReplicaSyncService> logger)
{
    public const string MechanismName = "full_table_copy";

    public async Task<ReplicaSyncBatchResult> SynchronizeAsync(
        string primaryDatabasePath,
        IReadOnlyList<ReplicaSyncTarget> targets,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryDatabasePath);
        ArgumentNullException.ThrowIfNull(targets);

        if (targets.Count == 0)
        {
            throw new ArgumentException("At least one replica target is required.", nameof(targets));
        }

        ReplicaSourceSnapshot snapshot = await CaptureSnapshotAsync(primaryDatabasePath, cancellationToken);
        Task<ReplicaSyncResult>[] replicaTasks = targets
            .Select(target => ApplySnapshotAsync(snapshot, target, cancellationToken))
            .ToArray();

        ReplicaSyncResult[] results = await Task.WhenAll(replicaTasks);

        return new ReplicaSyncBatchResult(
            PrimaryDatabasePath: Path.GetFullPath(primaryDatabasePath),
            SnapshotCapturedUtc: snapshot.CapturedUtc,
            Mechanism: MechanismName,
            Replicas: results);
    }

    private async Task<ReplicaSourceSnapshot> CaptureSnapshotAsync(
        string primaryDatabasePath,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string normalizedPrimaryPath = Path.GetFullPath(primaryDatabasePath);

        if (!File.Exists(normalizedPrimaryPath))
        {
            throw new FileNotFoundException($"Primary database '{normalizedPrimaryPath}' does not exist.", normalizedPrimaryPath);
        }

        await using PrimaryDbContext primaryDbContext = dbContextFactory.CreateDbContext(normalizedPrimaryPath);
        await using IDbContextTransaction transaction = await primaryDbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        ProductSnapshot[] products = await primaryDbContext.Products
            .AsNoTracking()
            .OrderBy(item => item.ProductId)
            .Select(item => new ProductSnapshot(
                item.ProductId,
                item.Name,
                item.Description,
                item.PriceCents,
                item.Category,
                item.Version,
                item.CreatedUtc,
                item.UpdatedUtc))
            .ToArrayAsync(cancellationToken);

        InventorySnapshot[] inventory = await primaryDbContext.Inventory
            .AsNoTracking()
            .OrderBy(item => item.ProductId)
            .Select(item => new InventorySnapshot(
                item.ProductId,
                item.AvailableQuantity,
                item.ReservedQuantity,
                item.Version,
                item.UpdatedUtc))
            .ToArrayAsync(cancellationToken);

        DateTimeOffset capturedUtc = timeProvider.GetUtcNow();
        await transaction.CommitAsync(cancellationToken);

        return new ReplicaSourceSnapshot(
            PrimaryDatabasePath: normalizedPrimaryPath,
            CapturedUtc: capturedUtc,
            SnapshotReadMs: stopwatch.Elapsed.TotalMilliseconds,
            Products: products,
            Inventory: inventory,
            LatestProductUpdatedUtc: products.Length > 0 ? products.Max(item => item.UpdatedUtc) : null,
            LatestInventoryUpdatedUtc: inventory.Length > 0 ? inventory.Max(item => item.UpdatedUtc) : null);
    }

    private async Task<ReplicaSyncResult> ApplySnapshotAsync(
        ReplicaSourceSnapshot snapshot,
        ReplicaSyncTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(target);

        string replicaDatabasePath = Path.GetFullPath(target.ReplicaDatabasePath);

        if (target.ConfiguredLag > TimeSpan.Zero)
        {
            await Task.Delay(target.ConfiguredLag, cancellationToken);
        }

        await databaseInitializer.InitializeAsync(replicaDatabasePath, cancellationToken);

        Stopwatch applyStopwatch = Stopwatch.StartNew();

        await using PrimaryDbContext replicaDbContext = dbContextFactory.CreateDbContext(replicaDatabasePath);
        await using IDbContextTransaction transaction = await replicaDbContext.Database.BeginTransactionAsync(cancellationToken);

        await replicaDbContext.Inventory.ExecuteDeleteAsync(cancellationToken);
        await replicaDbContext.Products.ExecuteDeleteAsync(cancellationToken);

        if (snapshot.Products.Length > 0)
        {
            await replicaDbContext.Products.AddRangeAsync(snapshot.Products.Select(CreateProductEntity), cancellationToken);
        }

        if (snapshot.Inventory.Length > 0)
        {
            await replicaDbContext.Inventory.AddRangeAsync(snapshot.Inventory.Select(CreateInventoryEntity), cancellationToken);
        }

        await replicaDbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        DateTimeOffset appliedUtc = timeProvider.GetUtcNow();
        ReplicaSyncResult result = new(
            ReplicaRegion: target.ReplicaRegion,
            ReplicaDatabasePath: replicaDatabasePath,
            Mechanism: MechanismName,
            SnapshotCapturedUtc: snapshot.CapturedUtc,
            AppliedUtc: appliedUtc,
            ConfiguredLagMs: target.ConfiguredLag.TotalMilliseconds,
            ObservedLagMs: (appliedUtc - snapshot.CapturedUtc).TotalMilliseconds,
            SnapshotReadMs: snapshot.SnapshotReadMs,
            ApplyMs: applyStopwatch.Elapsed.TotalMilliseconds,
            ProductCount: snapshot.Products.Length,
            InventoryRecordCount: snapshot.Inventory.Length,
            LatestProductUpdatedUtc: snapshot.LatestProductUpdatedUtc,
            LatestInventoryUpdatedUtc: snapshot.LatestInventoryUpdatedUtc);

        logger.LogInformation(
            "Replica sync {Mechanism} applied snapshot from primary {PrimaryDatabasePath} to replica {ReplicaDatabasePath} in region {ReplicaRegion}. SnapshotCapturedUtc={SnapshotCapturedUtc}, AppliedUtc={AppliedUtc}, ConfiguredLagMs={ConfiguredLagMs}, ObservedLagMs={ObservedLagMs}, SnapshotReadMs={SnapshotReadMs}, ApplyMs={ApplyMs}, Products={ProductCount}, Inventory={InventoryRecordCount}, LatestProductUpdatedUtc={LatestProductUpdatedUtc}, LatestInventoryUpdatedUtc={LatestInventoryUpdatedUtc}",
            result.Mechanism,
            snapshot.PrimaryDatabasePath,
            result.ReplicaDatabasePath,
            result.ReplicaRegion,
            result.SnapshotCapturedUtc,
            result.AppliedUtc,
            result.ConfiguredLagMs,
            result.ObservedLagMs,
            result.SnapshotReadMs,
            result.ApplyMs,
            result.ProductCount,
            result.InventoryRecordCount,
            result.LatestProductUpdatedUtc,
            result.LatestInventoryUpdatedUtc);

        return result;
    }

    private static Product CreateProductEntity(ProductSnapshot snapshot) =>
        new()
        {
            ProductId = snapshot.ProductId,
            Name = snapshot.Name,
            Description = snapshot.Description,
            PriceCents = snapshot.PriceCents,
            Category = snapshot.Category,
            Version = snapshot.Version,
            CreatedUtc = snapshot.CreatedUtc,
            UpdatedUtc = snapshot.UpdatedUtc
        };

    private static InventoryRecord CreateInventoryEntity(InventorySnapshot snapshot) =>
        new()
        {
            ProductId = snapshot.ProductId,
            AvailableQuantity = snapshot.AvailableQuantity,
            ReservedQuantity = snapshot.ReservedQuantity,
            Version = snapshot.Version,
            UpdatedUtc = snapshot.UpdatedUtc
        };

    private sealed record ReplicaSourceSnapshot(
        string PrimaryDatabasePath,
        DateTimeOffset CapturedUtc,
        double SnapshotReadMs,
        ProductSnapshot[] Products,
        InventorySnapshot[] Inventory,
        DateTimeOffset? LatestProductUpdatedUtc,
        DateTimeOffset? LatestInventoryUpdatedUtc);

    private sealed record ProductSnapshot(
        string ProductId,
        string Name,
        string Description,
        int PriceCents,
        string Category,
        long Version,
        DateTimeOffset CreatedUtc,
        DateTimeOffset UpdatedUtc);

    private sealed record InventorySnapshot(
        string ProductId,
        int AvailableQuantity,
        int ReservedQuantity,
        long Version,
        DateTimeOffset UpdatedUtc);
}
