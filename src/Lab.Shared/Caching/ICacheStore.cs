namespace Lab.Shared.Caching;

public interface ICacheStore
{
    ValueTask<CacheGetResult<T>> GetAsync<T>(CacheScope scope, string key, CancellationToken cancellationToken = default);

    ValueTask SetAsync<T>(CacheScope scope, string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    ValueTask<bool> InvalidateAsync(CacheScope scope, string key, CancellationToken cancellationToken = default);

    ValueTask<int> ExpireAsync(CancellationToken cancellationToken = default);
}
