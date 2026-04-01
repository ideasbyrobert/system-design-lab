namespace Storefront.Api.ProductPages;

public interface ICatalogProductClient
{
    Task<HttpResponseMessage> GetProductAsync(
        string productId,
        string runId,
        string correlationId,
        ProductReadSource readSource,
        bool debugTelemetryRequested,
        CatalogDependencyRoutePlan routePlan,
        CancellationToken cancellationToken);
}
