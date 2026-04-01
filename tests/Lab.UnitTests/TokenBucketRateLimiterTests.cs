using Lab.Shared.RateLimiting;

namespace Lab.UnitTests;

[TestClass]
public sealed class TokenBucketRateLimiterTests
{
    [TestMethod]
    public void TryConsume_AllowsBurstUpToCapacity_ThenRejects()
    {
        ManualTimeProvider timeProvider = new(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));
        InMemoryTokenBucketRateLimiter limiter = new(timeProvider);
        TokenBucketRateLimitPolicy policy = new("checkout", capacity: 2, tokensPerSecond: 2);

        TokenBucketRateLimitDecision first = limiter.TryConsume(policy, "/checkout", "user:user-0001");
        TokenBucketRateLimitDecision second = limiter.TryConsume(policy, "/checkout", "user:user-0001");
        TokenBucketRateLimitDecision third = limiter.TryConsume(policy, "/checkout", "user:user-0001");

        Assert.IsTrue(first.Allowed);
        Assert.IsTrue(second.Allowed);
        Assert.IsFalse(third.Allowed);
        Assert.AreEqual(1, third.RetryAfterSeconds);
        Assert.AreEqual(0d, third.TokensRemaining, 0.0001d);
    }

    [TestMethod]
    public void TryConsume_RefillsTokensBasedOnElapsedTime()
    {
        ManualTimeProvider timeProvider = new(new DateTimeOffset(2026, 4, 1, 1, 0, 0, TimeSpan.Zero));
        InMemoryTokenBucketRateLimiter limiter = new(timeProvider);
        TokenBucketRateLimitPolicy policy = new("checkout", capacity: 1, tokensPerSecond: 2);

        Assert.IsTrue(limiter.TryConsume(policy, "/checkout", "user:user-0001").Allowed);

        TokenBucketRateLimitDecision rejected = limiter.TryConsume(policy, "/checkout", "user:user-0001");
        Assert.IsFalse(rejected.Allowed);
        Assert.AreEqual(1, rejected.RetryAfterSeconds);

        timeProvider.Advance(TimeSpan.FromMilliseconds(250));
        TokenBucketRateLimitDecision stillRejected = limiter.TryConsume(policy, "/checkout", "user:user-0001");
        Assert.IsFalse(stillRejected.Allowed);

        timeProvider.Advance(TimeSpan.FromMilliseconds(250));
        TokenBucketRateLimitDecision allowedAfterRefill = limiter.TryConsume(policy, "/checkout", "user:user-0001");
        Assert.IsTrue(allowedAfterRefill.Allowed);
    }

    [TestMethod]
    public void TryConsume_UsesIndependentBucketsPerRouteAndPartition()
    {
        ManualTimeProvider timeProvider = new(new DateTimeOffset(2026, 4, 1, 2, 0, 0, TimeSpan.Zero));
        InMemoryTokenBucketRateLimiter limiter = new(timeProvider);
        TokenBucketRateLimitPolicy policy = new("checkout", capacity: 1, tokensPerSecond: 1);

        Assert.IsTrue(limiter.TryConsume(policy, "/checkout", "user:user-0001").Allowed);
        Assert.IsFalse(limiter.TryConsume(policy, "/checkout", "user:user-0001").Allowed);

        Assert.IsTrue(limiter.TryConsume(policy, "/checkout", "user:user-0002").Allowed);
        Assert.IsTrue(limiter.TryConsume(policy, "/products/sku-0001", "user:user-0001").Allowed);
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
