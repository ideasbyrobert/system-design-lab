using System.Text.Json;

namespace Storefront.Api.ProductPages;

internal static class CatalogProductResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<CatalogProductClientResult> ReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.StatusCode == System.Net.HttpStatusCode.OK)
        {
            CatalogProductResponsePayload payload = await DeserializeRequiredAsync<CatalogProductResponsePayload>(response, cancellationToken);

            StorefrontCatalogRequestInfo request = new(
                Service: "Catalog.Api",
                RunId: payload.Request.RunId,
                TraceId: payload.Request.TraceId,
                RequestId: payload.Request.RequestId,
                CorrelationId: payload.Request.CorrelationId);

            return new CatalogProductClientResult(
                Outcome: CatalogProductClientOutcome.Success,
                Product: new CatalogProductSnapshot(
                    ProductId: payload.ProductId,
                    Name: payload.Name,
                    Description: payload.Description,
                    Category: payload.Category,
                    PriceAmountCents: payload.Price.AmountCents,
                    PriceCurrencyCode: payload.Price.CurrencyCode,
                    PriceDisplay: payload.Price.Display,
                    AvailableQuantity: payload.Inventory.AvailableQuantity,
                    ReservedQuantity: payload.Inventory.ReservedQuantity,
                    SellableQuantity: payload.Inventory.SellableQuantity,
                    StockStatus: payload.Inventory.StockStatus,
                    Version: payload.Version,
                    ReadSource: NormalizeReadSource(payload.ReadSource),
                    Freshness: NormalizeFreshness(payload.Freshness, payload.ReadSource),
                    CatalogRequest: request)
                {
                    DebugTelemetry = payload.DebugTelemetry
                },
                CatalogRequest: request,
                ErrorCode: null,
                StatusCode: (int)response.StatusCode,
                DebugTelemetry: payload.DebugTelemetry,
                Freshness: NormalizeFreshness(payload.Freshness, payload.ReadSource));
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            CatalogProductNotFoundPayload payload = await DeserializeRequiredAsync<CatalogProductNotFoundPayload>(response, cancellationToken);

            return new CatalogProductClientResult(
                Outcome: CatalogProductClientOutcome.NotFound,
                Product: null,
                CatalogRequest: new StorefrontCatalogRequestInfo(
                    Service: "Catalog.Api",
                    RunId: payload.Request.RunId,
                    TraceId: payload.Request.TraceId,
                    RequestId: payload.Request.RequestId,
                    CorrelationId: payload.Request.CorrelationId),
                ErrorCode: payload.Error,
                StatusCode: (int)response.StatusCode,
                DebugTelemetry: payload.DebugTelemetry,
                Freshness: NormalizeFreshness(payload.Freshness, payload.ReadSource));
        }

        return new CatalogProductClientResult(
            Outcome: CatalogProductClientOutcome.Failed,
            Product: null,
            CatalogRequest: null,
            ErrorCode: $"catalog_http_{(int)response.StatusCode}",
            StatusCode: (int)response.StatusCode,
            DebugTelemetry: null,
            Freshness: null);
    }

    private static async Task<T> DeserializeRequiredAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
        where T : class
    {
        await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        T? payload = await JsonSerializer.DeserializeAsync<T>(responseStream, JsonOptions, cancellationToken);

        return payload ?? throw new JsonException($"Catalog response body could not be deserialized as {typeof(T).Name}.");
    }

    private sealed record CatalogRequestPayload(
        string RunId,
        string TraceId,
        string RequestId,
        string? CorrelationId);

    private sealed record CatalogPricePayload(
        int AmountCents,
        string CurrencyCode,
        string Display);

    private sealed record CatalogInventoryPayload(
        int AvailableQuantity,
        int ReservedQuantity,
        int SellableQuantity,
        string StockStatus);

    private sealed record CatalogProductResponsePayload(
        string ProductId,
        string Name,
        string Description,
        string Category,
        CatalogPricePayload Price,
        CatalogInventoryPayload Inventory,
        long Version,
        string? ReadSource,
        CatalogFreshnessPayload? Freshness,
        CatalogRequestPayload Request)
    {
        public CatalogDebugTelemetryInfo? DebugTelemetry { get; init; }
    }

    private sealed record CatalogProductNotFoundPayload(
        string Error,
        string ProductId,
        string? ReadSource,
        CatalogFreshnessPayload? Freshness,
        CatalogRequestPayload Request)
    {
        public CatalogDebugTelemetryInfo? DebugTelemetry { get; init; }
    }

    private sealed record CatalogFreshnessPayload(
        string ReadSource,
        int ComparedCount,
        int StaleCount,
        double? StaleFraction,
        double? MaxStalenessAgeMs,
        long? ObservedVersion,
        long? PrimaryVersion,
        DateTimeOffset? ObservedUpdatedUtc,
        DateTimeOffset? PrimaryUpdatedUtc);

    private static string NormalizeReadSource(string? readSource) =>
        string.IsNullOrWhiteSpace(readSource) ? ProductReadSource.Primary.ToText() : readSource.Trim();

    private static StorefrontReadFreshnessInfo NormalizeFreshness(
        CatalogFreshnessPayload? payload,
        string? readSource)
    {
        string normalizedReadSource = NormalizeReadSource(readSource);

        if (payload is null)
        {
            return new StorefrontReadFreshnessInfo(
                ReadSource: normalizedReadSource,
                ComparedCount: 0,
                StaleCount: 0,
                StaleFraction: null,
                MaxStalenessAgeMs: null,
                ObservedVersion: null,
                PrimaryVersion: null,
                ObservedUpdatedUtc: null,
                PrimaryUpdatedUtc: null);
        }

        return new StorefrontReadFreshnessInfo(
            ReadSource: NormalizeReadSource(payload.ReadSource),
            ComparedCount: payload.ComparedCount,
            StaleCount: payload.StaleCount,
            StaleFraction: payload.StaleFraction,
            MaxStalenessAgeMs: payload.MaxStalenessAgeMs,
            ObservedVersion: payload.ObservedVersion,
            PrimaryVersion: payload.PrimaryVersion,
            ObservedUpdatedUtc: payload.ObservedUpdatedUtc,
            PrimaryUpdatedUtc: payload.PrimaryUpdatedUtc);
    }
}
