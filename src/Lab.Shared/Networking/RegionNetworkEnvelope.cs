namespace Lab.Shared.Networking;

public sealed record RegionNetworkEnvelope(
    string CallerRegion,
    string TargetRegion,
    string NetworkScope,
    int InjectedDelayMs)
{
    public bool IsCrossRegion => string.Equals(NetworkScope, "cross-region", StringComparison.Ordinal);

    public TimeSpan InjectedDelay => TimeSpan.FromMilliseconds(InjectedDelayMs);
}
