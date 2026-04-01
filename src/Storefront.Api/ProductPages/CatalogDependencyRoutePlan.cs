using Lab.Shared.Configuration;
using Lab.Shared.Networking;

namespace Storefront.Api.ProductPages;

public sealed record CatalogDependencyRoutePlan(
    string RequestedBaseUrl,
    string RequestedTargetRegion,
    string EffectiveBaseUrl,
    string EffectiveTargetRegion,
    RegionNetworkEnvelope NetworkEnvelope,
    bool DegradedModeApplied,
    string? DegradedReason);

public static class CatalogDependencyRouteResolver
{
    public static CatalogDependencyRoutePlan Resolve(
        string callerRegion,
        ServiceEndpointOptions serviceEndpointOptions,
        RegionalDegradationOptions degradationOptions,
        IRegionNetworkEnvelopePolicy networkEnvelopePolicy)
    {
        ArgumentNullException.ThrowIfNull(serviceEndpointOptions);
        ArgumentNullException.ThrowIfNull(degradationOptions);
        ArgumentNullException.ThrowIfNull(networkEnvelopePolicy);

        string requestedBaseUrl = EnsureTrailingSlash(serviceEndpointOptions.CatalogBaseUrl);
        string requestedTargetRegion = Normalize(serviceEndpointOptions.CatalogRegion) ?? Normalize(callerRegion) ?? "local";
        string effectiveBaseUrl = requestedBaseUrl;
        string effectiveTargetRegion = requestedTargetRegion;
        bool degradedModeApplied = false;
        string? degradedReason = null;

        if (degradationOptions.SimulateLocalCatalogUnavailable &&
            !string.IsNullOrWhiteSpace(serviceEndpointOptions.CatalogFailoverBaseUrl))
        {
            effectiveBaseUrl = EnsureTrailingSlash(serviceEndpointOptions.CatalogFailoverBaseUrl!);
            effectiveTargetRegion = Normalize(serviceEndpointOptions.CatalogFailoverRegion) ?? requestedTargetRegion;
            degradedModeApplied = true;
            degradedReason = "local_catalog_unavailable";
        }

        RegionNetworkEnvelope networkEnvelope = networkEnvelopePolicy.Resolve(callerRegion, effectiveTargetRegion);

        return new CatalogDependencyRoutePlan(
            RequestedBaseUrl: requestedBaseUrl,
            RequestedTargetRegion: requestedTargetRegion,
            EffectiveBaseUrl: effectiveBaseUrl,
            EffectiveTargetRegion: effectiveTargetRegion,
            NetworkEnvelope: networkEnvelope,
            DegradedModeApplied: degradedModeApplied,
            DegradedReason: degradedReason);
    }

    private static string EnsureTrailingSlash(string value) =>
        value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
