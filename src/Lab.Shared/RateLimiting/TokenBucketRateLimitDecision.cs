namespace Lab.Shared.RateLimiting;

public sealed record TokenBucketRateLimitDecision(
    bool Allowed,
    string PolicyName,
    string Route,
    string PartitionKey,
    int RetryAfterSeconds,
    double TokensRemaining,
    int Capacity);
