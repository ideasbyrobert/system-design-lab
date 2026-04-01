using Lab.Shared.Caching;

namespace Lab.UnitTests;

[TestClass]
public sealed class InMemoryCacheStoreTests
{
    [TestMethod]
    public async Task CacheStore_GetSetExpireAndSnapshotMetrics_WorkTogether()
    {
        ManualTimeProvider timeProvider = new(new DateTimeOffset(2026, 03, 31, 15, 0, 0, TimeSpan.Zero));
        InMemoryCacheStore cacheStore = new(timeProvider, TimeSpan.FromSeconds(10), capacity: 8);
        CacheScope scope = CacheScope.Create("catalog-product-detail", "local");

        CacheGetResult<string> firstMiss = await cacheStore.GetAsync<string>(scope, "sku-0001");
        await cacheStore.SetAsync(scope, "sku-0001", "value-1", TimeSpan.FromSeconds(5));
        CacheGetResult<string> hit = await cacheStore.GetAsync<string>(scope, "sku-0001");

        timeProvider.Advance(TimeSpan.FromSeconds(6));

        CacheGetResult<string> expiredMiss = await cacheStore.GetAsync<string>(scope, "sku-0001");
        CacheMetricsSnapshot snapshot = cacheStore.GetSnapshot();

        Assert.IsFalse(firstMiss.Hit);
        Assert.IsTrue(hit.Hit);
        Assert.AreEqual("value-1", hit.Value);
        Assert.IsFalse(expiredMiss.Hit);
        Assert.AreEqual(1L, snapshot.HitCount);
        Assert.AreEqual(2L, snapshot.MissCount);
        Assert.AreEqual(0, snapshot.LiveEntryCount);
        Assert.IsNotNull(snapshot.AverageHitLookupMs);
        Assert.IsNotNull(snapshot.AverageMissLookupMs);
        Assert.HasCount(1, snapshot.Scopes);
        Assert.AreEqual("catalog-product-detail", snapshot.Scopes[0].NamespaceName);
        Assert.AreEqual("local", snapshot.Scopes[0].Region);
        Assert.AreEqual(1L, snapshot.Scopes[0].HitCount);
        Assert.AreEqual(2L, snapshot.Scopes[0].MissCount);
    }

    [TestMethod]
    public async Task CacheStore_Invalidate_RemovesOnlyTheTargetEntry_And_RegionsStayIsolated()
    {
        ManualTimeProvider timeProvider = new(new DateTimeOffset(2026, 03, 31, 15, 30, 0, TimeSpan.Zero));
        InMemoryCacheStore cacheStore = new(timeProvider, TimeSpan.FromSeconds(30), capacity: 8);
        CacheScope localScope = CacheScope.Create("catalog-product-detail", "local");
        CacheScope eastScope = CacheScope.Create("catalog-product-detail", "east");

        await cacheStore.SetAsync(localScope, "sku-0001", "local-value");
        await cacheStore.SetAsync(eastScope, "sku-0001", "east-value");

        bool removed = await cacheStore.InvalidateAsync(localScope, "sku-0001");
        CacheGetResult<string> localResult = await cacheStore.GetAsync<string>(localScope, "sku-0001");
        CacheGetResult<string> eastResult = await cacheStore.GetAsync<string>(eastScope, "sku-0001");

        Assert.IsTrue(removed);
        Assert.IsFalse(localResult.Hit);
        Assert.IsTrue(eastResult.Hit);
        Assert.AreEqual("east-value", eastResult.Value);
    }

    [TestMethod]
    public async Task CacheStore_ExpireAsync_RemovesExpiredEntriesProactively()
    {
        ManualTimeProvider timeProvider = new(new DateTimeOffset(2026, 03, 31, 16, 0, 0, TimeSpan.Zero));
        InMemoryCacheStore cacheStore = new(timeProvider, TimeSpan.FromSeconds(10), capacity: 8);
        CacheScope scope = CacheScope.Create("catalog-product-detail", "local");

        await cacheStore.SetAsync(scope, "sku-0001", "value-1", TimeSpan.FromSeconds(1));
        await cacheStore.SetAsync(scope, "sku-0002", "value-2", TimeSpan.FromSeconds(10));

        timeProvider.Advance(TimeSpan.FromSeconds(2));

        int expired = await cacheStore.ExpireAsync();
        CacheMetricsSnapshot snapshot = cacheStore.GetSnapshot();

        Assert.AreEqual(1, expired);
        Assert.AreEqual(1, snapshot.LiveEntryCount);
        Assert.AreEqual(1, snapshot.Scopes[0].LiveEntryCount);
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private long _timestamp;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan delta)
        {
            utcNow = utcNow.Add(delta);
            _timestamp += delta.Ticks;
        }
    }
}
