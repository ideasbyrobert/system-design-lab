using System.Collections.ObjectModel;

namespace Lab.Telemetry.RequestTracing;

public sealed record class StageTimingRecord
{
    public required string StageName { get; init; }

    public required DateTimeOffset StartedUtc { get; init; }

    public required DateTimeOffset EndedUtc { get; init; }

    public required double ElapsedMs { get; init; }

    public required string Outcome { get; init; }

    public IReadOnlyDictionary<string, string?> Metadata { get; init; } = EmptyMetadata;

    private static IReadOnlyDictionary<string, string?> EmptyMetadata { get; } =
        new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>());
}
