namespace Lab.Shared.Caching;

public sealed record CacheMetricsSnapshot
{
    public required int Capacity { get; init; }

    public required int LiveEntryCount { get; init; }

    public required long HitCount { get; init; }

    public required long MissCount { get; init; }

    public required double? AverageHitLookupMs { get; init; }

    public required double? AverageMissLookupMs { get; init; }

    public IReadOnlyList<CacheScopeMetricsSnapshot> Scopes { get; init; } = Array.Empty<CacheScopeMetricsSnapshot>();
}
