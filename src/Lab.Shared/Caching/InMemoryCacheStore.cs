namespace Lab.Shared.Caching;

public sealed class InMemoryCacheStore : ICacheStore, ICacheSnapshotProvider
{
    private readonly object _sync = new();
    private readonly Dictionary<CacheEntryKey, CacheEntry> _entries = [];
    private readonly Dictionary<CacheScopeKey, CacheMetricsAccumulator> _metricsByScope = [];
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _defaultTtl;

    public InMemoryCacheStore(TimeProvider? timeProvider = null, TimeSpan? defaultTtl = null, int capacity = 1024)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Cache capacity must be positive.");
        }

        _timeProvider = timeProvider ?? TimeProvider.System;
        _defaultTtl = defaultTtl ?? TimeSpan.FromSeconds(60);
        Capacity = capacity;
    }

    public int Capacity { get; }

    public ValueTask<CacheGetResult<T>> GetAsync<T>(CacheScope scope, string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(scope);

        string normalizedKey = NormalizeRequiredText(key, nameof(key));
        long startTimestamp = _timeProvider.GetTimestamp();
        CacheGetResult<T> result;
        bool hit;

        lock (_sync)
        {
            DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
            RemoveExpiredEntriesUnlocked(nowUtc);

            CacheEntryKey entryKey = new(scope.NamespaceName, scope.Region, normalizedKey);

            if (_entries.TryGetValue(entryKey, out CacheEntry? entry))
            {
                if (entry.Value is not T typedValue)
                {
                    throw new InvalidOperationException(
                        $"Cache entry '{normalizedKey}' in namespace '{scope.NamespaceName}' region '{scope.Region}' cannot be read as {typeof(T).FullName}.");
                }

                hit = true;
                result = new CacheGetResult<T>(Hit: true, Value: typedValue, ExpiresUtc: entry.ExpiresUtc);
            }
            else
            {
                hit = false;
                result = new CacheGetResult<T>(Hit: false, Value: default, ExpiresUtc: null);
            }

            double lookupMs = _timeProvider.GetElapsedTime(startTimestamp, _timeProvider.GetTimestamp()).TotalMilliseconds;
            GetMetricsAccumulatorUnlocked(new CacheScopeKey(scope.NamespaceName, scope.Region))
                .RecordLookup(hit, lookupMs);
        }

        return ValueTask.FromResult(result);
    }

    public ValueTask SetAsync<T>(
        CacheScope scope,
        string key,
        T value,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(value);

        TimeSpan effectiveTtl = ttl ?? _defaultTtl;

        if (effectiveTtl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "Cache TTL must be positive.");
        }

        string normalizedKey = NormalizeRequiredText(key, nameof(key));

        lock (_sync)
        {
            DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
            RemoveExpiredEntriesUnlocked(nowUtc);

            CacheEntryKey entryKey = new(scope.NamespaceName, scope.Region, normalizedKey);

            if (!_entries.ContainsKey(entryKey) && _entries.Count >= Capacity)
            {
                EvictOldestEntryUnlocked();
            }

            _entries[entryKey] = new CacheEntry(
                Value: value,
                ValueType: typeof(T),
                CreatedUtc: nowUtc,
                ExpiresUtc: nowUtc.Add(effectiveTtl));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> InvalidateAsync(CacheScope scope, string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(scope);

        string normalizedKey = NormalizeRequiredText(key, nameof(key));
        bool removed;

        lock (_sync)
        {
            DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
            RemoveExpiredEntriesUnlocked(nowUtc);
            removed = _entries.Remove(new CacheEntryKey(scope.NamespaceName, scope.Region, normalizedKey));
        }

        return ValueTask.FromResult(removed);
    }

    public ValueTask<int> ExpireAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int expired;

        lock (_sync)
        {
            expired = RemoveExpiredEntriesUnlocked(_timeProvider.GetUtcNow());
        }

        return ValueTask.FromResult(expired);
    }

    public CacheMetricsSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            RemoveExpiredEntriesUnlocked(_timeProvider.GetUtcNow());

            Dictionary<CacheScopeKey, int> liveEntryCounts = _entries.Keys
                .GroupBy(key => new CacheScopeKey(key.NamespaceName, key.Region))
                .ToDictionary(group => group.Key, group => group.Count());

            CacheScopeKey[] knownScopes = _metricsByScope.Keys
                .Concat(liveEntryCounts.Keys)
                .Distinct()
                .OrderBy(scope => scope.NamespaceName, StringComparer.Ordinal)
                .ThenBy(scope => scope.Region, StringComparer.Ordinal)
                .ToArray();

            CacheScopeMetricsSnapshot[] scopes = knownScopes
                .Select(scope =>
                {
                    int liveEntryCount = liveEntryCounts.TryGetValue(scope, out int count) ? count : 0;

                    return _metricsByScope.TryGetValue(scope, out CacheMetricsAccumulator? metrics)
                        ? metrics.ToSnapshot(scope, liveEntryCount)
                        : new CacheScopeMetricsSnapshot
                        {
                            NamespaceName = scope.NamespaceName,
                            Region = scope.Region,
                            LiveEntryCount = liveEntryCount,
                            HitCount = 0,
                            MissCount = 0,
                            AverageHitLookupMs = null,
                            AverageMissLookupMs = null
                        };
                })
                .ToArray();

            long hitCount = scopes.Sum(scope => scope.HitCount);
            long missCount = scopes.Sum(scope => scope.MissCount);
            double totalHitLookupMs = _metricsByScope.Values.Sum(metrics => metrics.TotalHitLookupMs);
            double totalMissLookupMs = _metricsByScope.Values.Sum(metrics => metrics.TotalMissLookupMs);

            return new CacheMetricsSnapshot
            {
                Capacity = Capacity,
                LiveEntryCount = _entries.Count,
                HitCount = hitCount,
                MissCount = missCount,
                AverageHitLookupMs = hitCount == 0 ? null : totalHitLookupMs / hitCount,
                AverageMissLookupMs = missCount == 0 ? null : totalMissLookupMs / missCount,
                Scopes = scopes
            };
        }
    }

    private int RemoveExpiredEntriesUnlocked(DateTimeOffset nowUtc)
    {
        CacheEntryKey[] expiredKeys = _entries
            .Where(pair => pair.Value.ExpiresUtc <= nowUtc)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (CacheEntryKey expiredKey in expiredKeys)
        {
            _entries.Remove(expiredKey);
        }

        return expiredKeys.Length;
    }

    private void EvictOldestEntryUnlocked()
    {
        if (_entries.Count == 0)
        {
            return;
        }

        CacheEntryKey oldestKey = _entries
            .OrderBy(pair => pair.Value.CreatedUtc)
            .ThenBy(pair => pair.Key.NamespaceName, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Region, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Key, StringComparer.Ordinal)
            .First()
            .Key;

        _entries.Remove(oldestKey);
    }

    private CacheMetricsAccumulator GetMetricsAccumulatorUnlocked(CacheScopeKey scopeKey)
    {
        if (_metricsByScope.TryGetValue(scopeKey, out CacheMetricsAccumulator? accumulator))
        {
            return accumulator;
        }

        accumulator = new CacheMetricsAccumulator();
        _metricsByScope[scopeKey] = accumulator;
        return accumulator;
    }

    private static string NormalizeRequiredText(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", paramName);
        }

        return value.Trim();
    }

    private sealed record CacheEntry(object Value, Type ValueType, DateTimeOffset CreatedUtc, DateTimeOffset ExpiresUtc);

    private readonly record struct CacheEntryKey(string NamespaceName, string Region, string Key);

    private readonly record struct CacheScopeKey(string NamespaceName, string Region);

    private sealed class CacheMetricsAccumulator
    {
        public long HitCount { get; private set; }

        public long MissCount { get; private set; }

        public double TotalHitLookupMs { get; private set; }

        public double TotalMissLookupMs { get; private set; }

        public void RecordLookup(bool hit, double lookupMs)
        {
            if (hit)
            {
                HitCount++;
                TotalHitLookupMs += lookupMs;
            }
            else
            {
                MissCount++;
                TotalMissLookupMs += lookupMs;
            }
        }

        public CacheScopeMetricsSnapshot ToSnapshot(CacheScopeKey scopeKey, int liveEntryCount) =>
            new()
            {
                NamespaceName = scopeKey.NamespaceName,
                Region = scopeKey.Region,
                LiveEntryCount = liveEntryCount,
                HitCount = HitCount,
                MissCount = MissCount,
                AverageHitLookupMs = HitCount == 0 ? null : TotalHitLookupMs / HitCount,
                AverageMissLookupMs = MissCount == 0 ? null : TotalMissLookupMs / MissCount
            };
    }
}
