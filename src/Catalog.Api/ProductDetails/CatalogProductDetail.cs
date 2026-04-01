namespace Catalog.Api.ProductDetails;

internal sealed record CatalogProductDetail(
    string ProductId,
    string Name,
    string Description,
    string Category,
    int PriceCents,
    string CurrencyCode,
    string DisplayPrice,
    int AvailableQuantity,
    int ReservedQuantity,
    int SellableQuantity,
    string StockStatus,
    long Version,
    string ReadSource,
    CatalogReadFreshnessInfo Freshness);
