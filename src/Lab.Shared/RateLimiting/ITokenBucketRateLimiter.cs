namespace Lab.Shared.RateLimiting;

public interface ITokenBucketRateLimiter
{
    TokenBucketRateLimitDecision TryConsume(
        TokenBucketRateLimitPolicy policy,
        string route,
        string partitionKey,
        int tokensRequested = 1);
}
