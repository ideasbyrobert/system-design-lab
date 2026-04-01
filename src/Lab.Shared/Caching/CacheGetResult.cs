namespace Lab.Shared.Caching;

public sealed record CacheGetResult<T>(
    bool Hit,
    T? Value,
    DateTimeOffset? ExpiresUtc);
