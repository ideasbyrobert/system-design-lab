using System.Text.Json.Serialization;

namespace Catalog.Api.ProductDetails;

internal sealed record CatalogRequestInfo(
    string RunId,
    string TraceId,
    string RequestId,
    string? CorrelationId);

internal sealed record CatalogPriceInfo(
    int AmountCents,
    string CurrencyCode,
    string Display);

internal sealed record CatalogInventoryInfo(
    int AvailableQuantity,
    int ReservedQuantity,
    int SellableQuantity,
    string StockStatus);

internal sealed record CatalogDebugStageInfo(
    string StageName,
    double ElapsedMs,
    string Outcome,
    IReadOnlyDictionary<string, string?> Metadata);

internal sealed record CatalogDebugTelemetryInfo(
    IReadOnlyList<CatalogDebugStageInfo> StageTimings,
    IReadOnlyList<string> Notes);

internal sealed record CatalogProductDetailResponse(
    string ProductId,
    string Name,
    string Description,
    string Category,
    CatalogPriceInfo Price,
    CatalogInventoryInfo Inventory,
    long Version,
    string ReadSource,
    CatalogReadFreshnessInfo Freshness,
    CatalogRequestInfo Request)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CatalogDebugTelemetryInfo? DebugTelemetry { get; init; }
}

internal sealed record CatalogProductNotFoundResponse(
    string Error,
    string ProductId,
    string ReadSource,
    CatalogReadFreshnessInfo Freshness,
    CatalogRequestInfo Request)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CatalogDebugTelemetryInfo? DebugTelemetry { get; init; }
}
