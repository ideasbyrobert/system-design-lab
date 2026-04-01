using System.Collections.Concurrent;

namespace Lab.Shared.RateLimiting;

public sealed class InMemoryTokenBucketRateLimiter(TimeProvider timeProvider) : ITokenBucketRateLimiter
{
    private readonly ConcurrentDictionary<string, BucketState> _buckets = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public TokenBucketRateLimitDecision TryConsume(
        TokenBucketRateLimitPolicy policy,
        string route,
        string partitionKey,
        int tokensRequested = 1)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (string.IsNullOrWhiteSpace(route))
        {
            throw new ArgumentException("Route is required.", nameof(route));
        }

        if (string.IsNullOrWhiteSpace(partitionKey))
        {
            throw new ArgumentException("Partition key is required.", nameof(partitionKey));
        }

        if (tokensRequested <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tokensRequested), "Requested token count must be positive.");
        }

        if (!policy.Enabled)
        {
            return new TokenBucketRateLimitDecision(
                Allowed: true,
                PolicyName: policy.Name,
                Route: route.Trim(),
                PartitionKey: partitionKey.Trim(),
                RetryAfterSeconds: 0,
                TokensRemaining: policy.Capacity,
                Capacity: policy.Capacity);
        }

        string normalizedRoute = route.Trim();
        string normalizedPartitionKey = partitionKey.Trim();
        string bucketKey = $"{policy.Name}|{normalizedRoute}|{normalizedPartitionKey}";
        long nowTimestamp = _timeProvider.GetTimestamp();
        BucketState bucket = _buckets.GetOrAdd(bucketKey, _ => new BucketState(policy.Capacity, nowTimestamp));

        lock (bucket.SyncRoot)
        {
            Refill(bucket, policy, nowTimestamp);

            if (bucket.AvailableTokens >= tokensRequested)
            {
                bucket.AvailableTokens -= tokensRequested;

                return new TokenBucketRateLimitDecision(
                    Allowed: true,
                    PolicyName: policy.Name,
                    Route: normalizedRoute,
                    PartitionKey: normalizedPartitionKey,
                    RetryAfterSeconds: 0,
                    TokensRemaining: bucket.AvailableTokens,
                    Capacity: policy.Capacity);
            }

            double missingTokens = tokensRequested - bucket.AvailableTokens;
            int retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(missingTokens / policy.TokensPerSecond));

            return new TokenBucketRateLimitDecision(
                Allowed: false,
                PolicyName: policy.Name,
                Route: normalizedRoute,
                PartitionKey: normalizedPartitionKey,
                RetryAfterSeconds: retryAfterSeconds,
                TokensRemaining: bucket.AvailableTokens,
                Capacity: policy.Capacity);
        }
    }

    private void Refill(BucketState bucket, TokenBucketRateLimitPolicy policy, long nowTimestamp)
    {
        if (nowTimestamp <= bucket.LastRefillTimestamp)
        {
            return;
        }

        TimeSpan elapsed = _timeProvider.GetElapsedTime(bucket.LastRefillTimestamp, nowTimestamp);
        if (elapsed <= TimeSpan.Zero)
        {
            return;
        }

        double replenishedTokens = elapsed.TotalSeconds * policy.TokensPerSecond;
        bucket.AvailableTokens = Math.Min(policy.Capacity, bucket.AvailableTokens + replenishedTokens);
        bucket.LastRefillTimestamp = nowTimestamp;
    }

    private sealed class BucketState(double availableTokens, long lastRefillTimestamp)
    {
        public object SyncRoot { get; } = new();

        public double AvailableTokens { get; set; } = availableTokens;

        public long LastRefillTimestamp { get; set; } = lastRefillTimestamp;
    }
}
