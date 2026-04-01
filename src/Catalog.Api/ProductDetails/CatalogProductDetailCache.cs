using Lab.Shared.Caching;
using Lab.Shared.Configuration;

namespace Catalog.Api.ProductDetails;

internal sealed class CatalogProductDetailCache(
    ICacheStore cacheStore,
    CacheOptions cacheOptions,
    EnvironmentLayout environmentLayout)
{
    private static readonly CacheScope ProductDetailScope = CacheScope.Create("catalog-product-detail", "local");

    public bool Enabled => cacheOptions.Enabled;

    public CacheScope Scope => ProductDetailScope with { Region = environmentLayout.CurrentRegion };

    public async Task<CatalogProductCacheLookupResult> GetAsync(
        string readSource,
        string productId,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return new CatalogProductCacheLookupResult(CatalogProductCacheLookupOutcome.Disabled, null, null);
        }

        CacheGetResult<CatalogProductDetail> result = await cacheStore.GetAsync<CatalogProductDetail>(
            Scope,
            CreateCacheKey(readSource, productId),
            cancellationToken);

        return new CatalogProductCacheLookupResult(
            result.Hit ? CatalogProductCacheLookupOutcome.Hit : CatalogProductCacheLookupOutcome.Miss,
            result.Value,
            result.ExpiresUtc);
    }

    public async Task SetAsync(
        string readSource,
        string productId,
        CatalogProductDetail productDetail,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return;
        }

        await cacheStore.SetAsync(
            Scope,
            CreateCacheKey(readSource, productId),
            productDetail,
            TimeSpan.FromSeconds(Math.Max(cacheOptions.DefaultTtlSeconds, 1)),
            cancellationToken);
    }

    private static string CreateCacheKey(string readSource, string productId) =>
        $"{readSource.Trim().ToLowerInvariant()}::{productId}";
}
