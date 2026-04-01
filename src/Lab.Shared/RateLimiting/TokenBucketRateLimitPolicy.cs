namespace Lab.Shared.RateLimiting;

public sealed class TokenBucketRateLimitPolicy
{
    public TokenBucketRateLimitPolicy(
        string name,
        int capacity,
        double tokensPerSecond,
        bool enabled = true)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Policy name is required.", nameof(name));
        }

        if (enabled && capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Enabled token-bucket policies require a positive capacity.");
        }

        if (enabled && tokensPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tokensPerSecond), "Enabled token-bucket policies require a positive refill rate.");
        }

        Name = name.Trim();
        Capacity = capacity;
        TokensPerSecond = tokensPerSecond;
        Enabled = enabled;
    }

    public string Name { get; }

    public int Capacity { get; }

    public double TokensPerSecond { get; }

    public bool Enabled { get; }
}
