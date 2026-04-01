namespace Catalog.Api.ProductDetails;

internal sealed record CatalogProductCacheLookupResult(
    CatalogProductCacheLookupOutcome Outcome,
    CatalogProductDetail? Product,
    DateTimeOffset? ExpiresUtc)
{
    public bool CacheHit => Outcome == CatalogProductCacheLookupOutcome.Hit;
}
