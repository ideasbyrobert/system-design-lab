namespace Lab.Telemetry.RequestTracing;

public sealed record class DependencyCallRecord
{
    public required string DependencyName { get; init; }

    public required string Route { get; init; }

    public required string Region { get; init; }

    public required DateTimeOffset StartedUtc { get; init; }

    public required DateTimeOffset EndedUtc { get; init; }

    public required double ElapsedMs { get; init; }

    public int? StatusCode { get; init; }

    public required string Outcome { get; init; }

    public IReadOnlyDictionary<string, string?> Metadata { get; init; } = new Dictionary<string, string?>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}
