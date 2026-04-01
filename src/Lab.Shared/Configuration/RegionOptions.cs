namespace Lab.Shared.Configuration;

public sealed class RegionOptions
{
    public string CurrentRegion { get; set; } = "local";

    public string PrimaryRegion { get; set; } = "local";

    public string EastReplicaRegion { get; set; } = "us-east";

    public string WestReplicaRegion { get; set; } = "us-west";

    public int SameRegionLatencyMs { get; set; } = 0;

    public int CrossRegionLatencyMs { get; set; } = 25;
}
