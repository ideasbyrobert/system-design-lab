using System.Text.Json;
using Lab.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lab.Persistence.Projections;

public sealed class ProductPageProjectionRebuilder(
    PrimaryDatabaseInitializer primaryDatabaseInitializer,
    PrimaryDbContextFactory primaryDbContextFactory,
    ReadModelDatabaseInitializer readModelDatabaseInitializer,
    ReadModelDbContextFactory readModelDbContextFactory,
    TimeProvider timeProvider)
{
    private const string CurrencyCode = "USD";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ProductPageProjectionRebuildResult> RebuildAsync(
        string primaryDatabasePath,
        string readModelDatabasePath,
        string region,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryDatabasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(readModelDatabasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(region);

        string normalizedRegion = region.Trim();
        string normalizedReadModelPath = Path.GetFullPath(readModelDatabasePath);

        await primaryDatabaseInitializer.InitializeAsync(primaryDatabasePath, cancellationToken);
        await readModelDatabaseInitializer.InitializeAsync(normalizedReadModelPath, cancellationToken);

        await using PrimaryDbContext primaryDbContext = primaryDbContextFactory.CreateDbContext(primaryDatabasePath);

        SourceProductPageRow[] sourceRows = await primaryDbContext.Products
            .AsNoTracking()
            .OrderBy(product => product.ProductId)
            .Select(product => new SourceProductPageRow(
                product.ProductId,
                product.Name,
                product.Description,
                product.Category,
                product.PriceCents,
                product.Version,
                product.Inventory == null ? 0 : product.Inventory.AvailableQuantity,
                product.Inventory == null ? 0 : product.Inventory.ReservedQuantity,
                product.Inventory == null ? 0 : product.Inventory.Version))
            .ToArrayAsync(cancellationToken);

        DateTimeOffset projectedUtc = timeProvider.GetUtcNow();
        ReadModelProductPage[] projectionRows = sourceRows
            .Select(sourceRow => CreateProjectionRow(sourceRow, normalizedRegion, projectedUtc))
            .ToArray();

        await using ReadModelDbContext readModelDbContext = readModelDbContextFactory.CreateDbContext(normalizedReadModelPath);
        await using var transaction = await readModelDbContext.Database.BeginTransactionAsync(cancellationToken);

        await readModelDbContext.ProductPages
            .Where(row => row.Region == normalizedRegion)
            .ExecuteDeleteAsync(cancellationToken);

        await readModelDbContext.ProductPages.AddRangeAsync(projectionRows, cancellationToken);
        await readModelDbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ProductPageProjectionRebuildResult(
            ReadModelDatabasePath: normalizedReadModelPath,
            Region: normalizedRegion,
            RowsWritten: projectionRows.Length,
            ProjectedUtc: projectedUtc);
    }

    private static ReadModelProductPage CreateProjectionRow(
        SourceProductPageRow sourceRow,
        string region,
        DateTimeOffset projectedUtc)
    {
        int sellableQuantity = Math.Max(sourceRow.AvailableQuantity - sourceRow.ReservedQuantity, 0);
        long projectionVersion = Math.Max(sourceRow.ProductVersion, sourceRow.InventoryVersion);

        ProductPageProjectionSummary summary = new(
            ProductId: sourceRow.ProductId,
            Name: sourceRow.Name,
            Description: sourceRow.Description,
            Category: sourceRow.Category,
            Price: new ProductPageProjectionPriceSummary(
                AmountCents: sourceRow.PriceCents,
                CurrencyCode: CurrencyCode,
                Display: FormatPrice(sourceRow.PriceCents)),
            Inventory: new ProductPageProjectionInventorySummary(
                AvailableQuantity: sourceRow.AvailableQuantity,
                ReservedQuantity: sourceRow.ReservedQuantity,
                SellableQuantity: sellableQuantity,
                StockStatus: DetermineStockStatus(sellableQuantity)),
            Versions: new ProductPageProjectionSourceVersions(
                ProductVersion: sourceRow.ProductVersion,
                InventoryVersion: sourceRow.InventoryVersion,
                ProjectionVersion: projectionVersion));

        return new ReadModelProductPage
        {
            ProductId = sourceRow.ProductId,
            Region = region,
            ProjectionVersion = projectionVersion,
            SummaryJson = JsonSerializer.Serialize(summary, JsonOptions),
            ProjectedUtc = projectedUtc
        };
    }

    private static string DetermineStockStatus(int sellableQuantity) =>
        sellableQuantity switch
        {
            <= 0 => "out_of_stock",
            <= 5 => "low_stock",
            _ => "in_stock"
        };

    private static string FormatPrice(int amountCents)
    {
        decimal amount = amountCents / 100m;
        return amount.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
    }

    private sealed record SourceProductPageRow(
        string ProductId,
        string Name,
        string Description,
        string Category,
        int PriceCents,
        long ProductVersion,
        int AvailableQuantity,
        int ReservedQuantity,
        long InventoryVersion);

    private sealed record ProductPageProjectionPriceSummary(
        int AmountCents,
        string CurrencyCode,
        string Display);

    private sealed record ProductPageProjectionInventorySummary(
        int AvailableQuantity,
        int ReservedQuantity,
        int SellableQuantity,
        string StockStatus);

    private sealed record ProductPageProjectionSourceVersions(
        long ProductVersion,
        long InventoryVersion,
        long ProjectionVersion);

    private sealed record ProductPageProjectionSummary(
        string ProductId,
        string Name,
        string Description,
        string Category,
        ProductPageProjectionPriceSummary Price,
        ProductPageProjectionInventorySummary Inventory,
        ProductPageProjectionSourceVersions Versions);
}
