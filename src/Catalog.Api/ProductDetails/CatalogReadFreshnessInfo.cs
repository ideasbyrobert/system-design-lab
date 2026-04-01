namespace Catalog.Api.ProductDetails;

internal sealed record CatalogReadFreshnessInfo(
    string ReadSource,
    int ComparedCount,
    int StaleCount,
    double? StaleFraction,
    double? MaxStalenessAgeMs,
    long? ObservedVersion,
    long? PrimaryVersion,
    DateTimeOffset? ObservedUpdatedUtc,
    DateTimeOffset? PrimaryUpdatedUtc)
{
    public bool StaleRead => StaleCount > 0;
}

internal sealed record CatalogProductReadResult(
    CatalogProductDetail? Product,
    CatalogReadFreshnessInfo Freshness,
    Lab.Shared.Networking.RegionNetworkEnvelope ReadEnvelope);
