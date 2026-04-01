using LoadGenTool.Cli;

namespace LoadGenTool.Workloads;

public static class LoadSchedulePlanner
{
    public static IReadOnlyList<TimeSpan> CreateOffsets(LoadGenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Mode switch
        {
            WorkloadMode.Constant => CreateConstantOffsets(options.RequestsPerSecond, options.Duration),
            WorkloadMode.Burst => CreateBurstOffsets(
                options.BurstSize ?? ResolveDefaultBurstSize(options),
                options.BurstPeriod,
                options.Duration),
            _ => throw new InvalidOperationException($"Unsupported workload mode '{options.Mode}'.")
        };
    }

    private static IReadOnlyList<TimeSpan> CreateConstantOffsets(double requestsPerSecond, TimeSpan duration)
    {
        double intervalMs = 1000d / requestsPerSecond;
        List<TimeSpan> offsets = [];

        for (double nextOffsetMs = 0d; nextOffsetMs < duration.TotalMilliseconds; nextOffsetMs += intervalMs)
        {
            offsets.Add(TimeSpan.FromMilliseconds(nextOffsetMs));
        }

        return offsets;
    }

    private static IReadOnlyList<TimeSpan> CreateBurstOffsets(int burstSize, TimeSpan burstPeriod, TimeSpan duration)
    {
        List<TimeSpan> offsets = [];

        for (TimeSpan burstStart = TimeSpan.Zero; burstStart < duration; burstStart += burstPeriod)
        {
            for (int index = 0; index < burstSize; index++)
            {
                offsets.Add(burstStart);
            }
        }

        return offsets;
    }

    private static int ResolveDefaultBurstSize(LoadGenOptions options)
    {
        int burstSize = (int)Math.Round(
            options.RequestsPerSecond * options.BurstPeriod.TotalSeconds,
            MidpointRounding.AwayFromZero);

        return Math.Max(1, burstSize);
    }
}
