namespace Lab.Shared.Networking;

public interface IRegionNetworkEnvelopePolicy
{
    RegionNetworkEnvelope Resolve(string? targetRegion);

    RegionNetworkEnvelope Resolve(string callerRegion, string? targetRegion);
}
