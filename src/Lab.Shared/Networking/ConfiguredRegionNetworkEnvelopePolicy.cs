using Lab.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace Lab.Shared.Networking;

public sealed class ConfiguredRegionNetworkEnvelopePolicy(
    EnvironmentLayout environmentLayout,
    IOptions<RegionOptions> regionOptions) : IRegionNetworkEnvelopePolicy
{
    private readonly string _defaultCallerRegion = NormalizeRegion(environmentLayout.CurrentRegion) ?? "local";
    private readonly int _sameRegionLatencyMs = NormalizeLatency(regionOptions.Value.SameRegionLatencyMs);
    private readonly int _crossRegionLatencyMs = NormalizeLatency(regionOptions.Value.CrossRegionLatencyMs);

    public RegionNetworkEnvelope Resolve(string? targetRegion) =>
        Resolve(_defaultCallerRegion, targetRegion);

    public RegionNetworkEnvelope Resolve(string callerRegion, string? targetRegion)
    {
        string normalizedCallerRegion = NormalizeRegion(callerRegion) ?? _defaultCallerRegion;
        string normalizedTargetRegion = NormalizeRegion(targetRegion) ?? normalizedCallerRegion;
        bool isSameRegion = string.Equals(normalizedCallerRegion, normalizedTargetRegion, StringComparison.OrdinalIgnoreCase);

        return new RegionNetworkEnvelope(
            CallerRegion: normalizedCallerRegion,
            TargetRegion: normalizedTargetRegion,
            NetworkScope: isSameRegion ? "same-region" : "cross-region",
            InjectedDelayMs: isSameRegion ? _sameRegionLatencyMs : _crossRegionLatencyMs);
    }

    private static int NormalizeLatency(int latencyMs) => latencyMs < 0 ? 0 : latencyMs;

    private static string? NormalizeRegion(string? region) =>
        string.IsNullOrWhiteSpace(region) ? null : region.Trim();
}
