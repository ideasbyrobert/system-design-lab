namespace Lab.Shared.Configuration;

public sealed class ServiceEndpointOptions
{
    public string CatalogBaseUrl { get; set; } = "http://localhost:5203";

    public string? CatalogRegion { get; set; }

    public string? CatalogFailoverBaseUrl { get; set; }

    public string? CatalogFailoverRegion { get; set; }

    public string CartBaseUrl { get; set; } = "http://localhost:5204";

    public string? CartRegion { get; set; }

    public string OrderBaseUrl { get; set; } = "http://localhost:5205";

    public string? OrderRegion { get; set; }

    public string PaymentSimulatorBaseUrl { get; set; } = "http://localhost:5206";

    public string? PaymentSimulatorRegion { get; set; }
}
