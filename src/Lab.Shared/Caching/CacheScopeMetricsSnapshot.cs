namespace Lab.Shared.Caching;

public sealed record CacheScopeMetricsSnapshot
{
    public required string NamespaceName { get; init; }

    public required string Region { get; init; }

    public required int LiveEntryCount { get; init; }

    public required long HitCount { get; init; }

    public required long MissCount { get; init; }

    public required double? AverageHitLookupMs { get; init; }

    public required double? AverageMissLookupMs { get; init; }
}
