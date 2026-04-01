using Lab.Shared.Caching;
using Lab.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace Storefront.Api.ProductPages;

internal sealed class StorefrontProductDetailCache
{
    private readonly ICacheStore _cacheStore;
    private readonly CacheOptions _options;

    public StorefrontProductDetailCache(
        ICacheStore cacheStore,
        IOptions<CacheOptions> optionsAccessor,
        EnvironmentLayout layout)
    {
        _cacheStore = cacheStore;
        _options = optionsAccessor.Value;
        Scope = CacheScope.Create("storefront-product-page", layout.CurrentRegion);
    }

    public CacheScope Scope { get; }

    public ValueTask<CacheGetResult<CatalogProductSnapshot>> GetAsync(
        string readSource,
        string productId,
        CancellationToken cancellationToken = default) =>
        _cacheStore.GetAsync<CatalogProductSnapshot>(Scope, CreateCacheKey(readSource, productId), cancellationToken);

    public ValueTask SetAsync(
        string readSource,
        CatalogProductSnapshot product,
        CancellationToken cancellationToken = default) =>
        _cacheStore.SetAsync(
            Scope,
            CreateCacheKey(readSource, product.ProductId),
            product,
            TimeSpan.FromSeconds(Math.Max(_options.DefaultTtlSeconds, 1)),
            cancellationToken);

    private static string CreateCacheKey(string readSource, string productId) =>
        $"{readSource.Trim().ToLowerInvariant()}::{productId}";
}
