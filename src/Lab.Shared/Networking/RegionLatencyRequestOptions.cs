namespace Lab.Shared.Networking;

public static class RegionLatencyRequestOptions
{
    public static readonly HttpRequestOptionsKey<string> TargetRegion = new("lab.target-region");
}
