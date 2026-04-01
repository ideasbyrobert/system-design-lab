using System.Text.Json.Serialization;

namespace Storefront.Api.ProductPages;

internal sealed record StorefrontRequestInfo(
    string RunId,
    string TraceId,
    string RequestId,
    string? CorrelationId);

internal sealed record StorefrontCatalogRequestInfo(
    string Service,
    string RunId,
    string TraceId,
    string RequestId,
    string? CorrelationId);

internal sealed record StorefrontPriceInfo(
    int AmountCents,
    string CurrencyCode,
    string Display);

internal sealed record StorefrontInventoryInfo(
    int AvailableQuantity,
    int ReservedQuantity,
    int SellableQuantity,
    string StockStatus);

internal sealed record StorefrontCacheInfo(
    string Mode,
    bool Hit,
    string NamespaceName,
    string Region);

internal sealed record StorefrontReadFreshnessInfo(
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

internal sealed record StorefrontDebugStageInfo(
    string StageName,
    double ElapsedMs,
    string Outcome,
    IReadOnlyDictionary<string, string?> Metadata);

internal sealed record StorefrontDebugDependencyInfo(
    string DependencyName,
    string Route,
    string Region,
    double ElapsedMs,
    int? StatusCode,
    string Outcome,
    IReadOnlyList<string> Notes);

internal sealed record CatalogDebugStageInfo(
    string StageName,
    double ElapsedMs,
    string Outcome,
    IReadOnlyDictionary<string, string?> Metadata);

internal sealed record CatalogDebugTelemetryInfo(
    IReadOnlyList<CatalogDebugStageInfo> StageTimings,
    IReadOnlyList<string> Notes);

internal sealed record StorefrontDebugTelemetryInfo(
    IReadOnlyList<StorefrontDebugStageInfo> StageTimings,
    IReadOnlyList<StorefrontDebugDependencyInfo> DependencyCalls,
    IReadOnlyList<string> Notes)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CatalogDebugTelemetryInfo? Catalog { get; init; }
}

internal sealed record CatalogProductSnapshot(
    string ProductId,
    string Name,
    string Description,
    string Category,
    int PriceAmountCents,
    string PriceCurrencyCode,
    string PriceDisplay,
    int AvailableQuantity,
    int ReservedQuantity,
    int SellableQuantity,
    string StockStatus,
    long Version,
    string ReadSource,
    StorefrontReadFreshnessInfo Freshness,
    StorefrontCatalogRequestInfo CatalogRequest)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CatalogDebugTelemetryInfo? DebugTelemetry { get; init; }
}

internal enum CatalogProductClientOutcome
{
    Success,
    NotFound,
    Failed
}

internal sealed record CatalogProductClientResult(
    CatalogProductClientOutcome Outcome,
    CatalogProductSnapshot? Product,
    StorefrontCatalogRequestInfo? CatalogRequest,
    string? ErrorCode,
    int StatusCode,
    CatalogDebugTelemetryInfo? DebugTelemetry,
    StorefrontReadFreshnessInfo? Freshness);

internal sealed record ProductPageResponse(
    string ProductId,
    string Name,
    string Description,
    string Category,
    StorefrontPriceInfo Price,
    StorefrontInventoryInfo Inventory,
    long Version,
    string Source,
    string ReadSource,
    StorefrontReadFreshnessInfo Freshness,
    StorefrontCacheInfo Cache,
    StorefrontCatalogRequestInfo Catalog,
    StorefrontRequestInfo Request)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StorefrontDebugTelemetryInfo? DebugTelemetry { get; init; }
}

internal sealed record ProductPageNotFoundResponse(
    string Error,
    string ProductId,
    string Source,
    string ReadSource,
    StorefrontReadFreshnessInfo? Freshness,
    StorefrontCacheInfo Cache,
    StorefrontCatalogRequestInfo? Catalog,
    StorefrontRequestInfo Request)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StorefrontDebugTelemetryInfo? DebugTelemetry { get; init; }
}

internal sealed record ProductPageFailureResponse(
    string Error,
    string ProductId,
    string Source,
    string ReadSource,
    StorefrontReadFreshnessInfo? Freshness,
    StorefrontCacheInfo Cache,
    StorefrontCatalogRequestInfo? Catalog,
    StorefrontRequestInfo Request)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StorefrontDebugTelemetryInfo? DebugTelemetry { get; init; }
}
