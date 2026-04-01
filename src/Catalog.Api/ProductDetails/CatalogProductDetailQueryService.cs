using Lab.Persistence;
using Lab.Shared.Configuration;
using Lab.Shared.Networking;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.ProductDetails;

internal sealed class CatalogProductDetailQueryService(
    EnvironmentLayout layout,
    PrimaryDbContextFactory dbContextFactory,
    PrimaryDatabaseInitializer databaseInitializer,
    IRegionNetworkEnvelopePolicy regionNetworkEnvelopePolicy,
    TimeProvider timeProvider)
{
    private const string CurrencyCode = "USD";

    public async Task<CatalogProductReadResult> GetByIdAsync(
        string productId,
        CatalogProductReadTarget readTarget,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentNullException.ThrowIfNull(readTarget);

        RegionNetworkEnvelope readEnvelope = ResolveReadEnvelope(readTarget);

        if (readEnvelope.InjectedDelay > TimeSpan.Zero)
        {
            await Task.Delay(readEnvelope.InjectedDelay, cancellationToken);
        }

        await databaseInitializer.InitializeAsync(readTarget.DatabasePath, cancellationToken);

        ProductDetailRow? observedRow;
        ProductDetailRow? primaryRow;

        await using (PrimaryDbContext observedDbContext = dbContextFactory.CreateDbContext(readTarget.DatabasePath))
        {
            observedRow = await QueryRowAsync(observedDbContext, productId, cancellationToken);
        }

        if (readTarget.ReadSource == CatalogProductReadSource.Primary)
        {
            primaryRow = observedRow;
        }
        else
        {
            await databaseInitializer.InitializeAsync(layout.PrimaryDatabasePath, cancellationToken);
            await using PrimaryDbContext primaryDbContext = dbContextFactory.CreateDbContext(layout.PrimaryDatabasePath);
            primaryRow = await QueryRowAsync(primaryDbContext, productId, cancellationToken);
        }

        CatalogReadFreshnessInfo freshness = CreateFreshnessInfo(
            readTarget.ReadSourceText,
            observedRow,
            primaryRow,
            timeProvider.GetUtcNow());

        if (observedRow is null)
        {
            return new CatalogProductReadResult(null, freshness, readEnvelope);
        }

        int sellableQuantity = Math.Max(observedRow.AvailableQuantity - observedRow.ReservedQuantity, 0);

        return new CatalogProductReadResult(
            new CatalogProductDetail(
                ProductId: observedRow.ProductId,
                Name: observedRow.Name,
                Description: observedRow.Description,
                Category: observedRow.Category,
                PriceCents: observedRow.PriceCents,
                CurrencyCode: CurrencyCode,
                DisplayPrice: FormatPrice(observedRow.PriceCents),
                AvailableQuantity: observedRow.AvailableQuantity,
                ReservedQuantity: observedRow.ReservedQuantity,
                SellableQuantity: sellableQuantity,
                StockStatus: DetermineStockStatus(sellableQuantity),
                Version: observedRow.ProductVersion,
                ReadSource: readTarget.ReadSourceText,
                Freshness: freshness),
            freshness,
            readEnvelope);
    }

    private RegionNetworkEnvelope ResolveReadEnvelope(CatalogProductReadTarget readTarget)
    {
        if (string.Equals(readTarget.SelectionScope, "cross-region", StringComparison.OrdinalIgnoreCase))
        {
            return regionNetworkEnvelopePolicy.Resolve(layout.CurrentRegion, readTarget.TargetRegion);
        }

        return new RegionNetworkEnvelope(
            CallerRegion: layout.CurrentRegion,
            TargetRegion: readTarget.TargetRegion,
            NetworkScope: readTarget.SelectionScope,
            InjectedDelayMs: 0);
    }

    private static async Task<ProductDetailRow?> QueryRowAsync(
        PrimaryDbContext dbContext,
        string productId,
        CancellationToken cancellationToken) =>
        await dbContext.Products
            .AsNoTracking()
            .Where(product => product.ProductId == productId)
            .Select(product => new ProductDetailRow(
                product.ProductId,
                product.Name,
                product.Description,
                product.Category,
                product.PriceCents,
                product.Version,
                product.UpdatedUtc,
                product.Inventory == null ? 0 : product.Inventory.AvailableQuantity,
                product.Inventory == null ? 0 : product.Inventory.ReservedQuantity,
                product.Inventory == null ? 0 : product.Inventory.Version,
                product.Inventory == null ? product.UpdatedUtc : product.Inventory.UpdatedUtc))
            .SingleOrDefaultAsync(cancellationToken);

    private static CatalogReadFreshnessInfo CreateFreshnessInfo(
        string readSource,
        ProductDetailRow? observedRow,
        ProductDetailRow? primaryRow,
        DateTimeOffset comparisonUtc)
    {
        int comparedCount = observedRow is null && primaryRow is null ? 0 : 1;
        bool staleRead =
            !string.Equals(readSource, CatalogProductReadSource.Primary.ToText(), StringComparison.Ordinal) &&
            (
                (observedRow is null) != (primaryRow is null) ||
                (observedRow is not null &&
                 primaryRow is not null &&
                 (observedRow.SourceVersion < primaryRow.SourceVersion ||
                  observedRow.SourceUpdatedUtc < primaryRow.SourceUpdatedUtc))
            );

        double? maxStalenessAgeMs = null;

        if (staleRead)
        {
            if (observedRow is not null && primaryRow is not null)
            {
                maxStalenessAgeMs = Math.Max(0d, (primaryRow.SourceUpdatedUtc - observedRow.SourceUpdatedUtc).TotalMilliseconds);
            }
            else if (primaryRow is not null)
            {
                maxStalenessAgeMs = Math.Max(0d, (comparisonUtc - primaryRow.SourceUpdatedUtc).TotalMilliseconds);
            }
        }

        return new CatalogReadFreshnessInfo(
            ReadSource: readSource,
            ComparedCount: comparedCount,
            StaleCount: staleRead ? 1 : 0,
            StaleFraction: comparedCount > 0 ? (staleRead ? 1d : 0d) : null,
            MaxStalenessAgeMs: maxStalenessAgeMs,
            ObservedVersion: observedRow?.SourceVersion,
            PrimaryVersion: primaryRow?.SourceVersion,
            ObservedUpdatedUtc: observedRow?.SourceUpdatedUtc,
            PrimaryUpdatedUtc: primaryRow?.SourceUpdatedUtc);
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

    private sealed record ProductDetailRow(
        string ProductId,
        string Name,
        string Description,
        string Category,
        int PriceCents,
        long ProductVersion,
        DateTimeOffset ProductUpdatedUtc,
        int AvailableQuantity,
        int ReservedQuantity,
        long InventoryVersion,
        DateTimeOffset InventoryUpdatedUtc)
    {
        public long SourceVersion => Math.Max(ProductVersion, InventoryVersion);

        public DateTimeOffset SourceUpdatedUtc =>
            ProductUpdatedUtc >= InventoryUpdatedUtc ? ProductUpdatedUtc : InventoryUpdatedUtc;
    }
}
