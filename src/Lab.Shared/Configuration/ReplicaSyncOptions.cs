namespace Lab.Shared.Configuration;

public sealed class ReplicaSyncOptions
{
    public int EastLagMilliseconds { get; set; }

    public int WestLagMilliseconds { get; set; }
}
