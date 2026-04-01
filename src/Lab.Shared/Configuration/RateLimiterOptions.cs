namespace Lab.Shared.Configuration;

public sealed class RateLimiterOptions
{
    public int TokenBucketCapacity { get; set; } = 100;

    public int TokensPerSecond { get; set; } = 50;

    public int QueueLimit { get; set; } = 0;

    public TokenBucketPolicyOptions Checkout { get; set; } = new();
}
