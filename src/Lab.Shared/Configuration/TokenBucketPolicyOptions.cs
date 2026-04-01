namespace Lab.Shared.Configuration;

public sealed class TokenBucketPolicyOptions
{
    public bool Enabled { get; set; } = true;

    public int? TokenBucketCapacity { get; set; }

    public int? TokensPerSecond { get; set; }

    public int ResolveCapacity(int fallbackCapacity) =>
        TokenBucketCapacity is > 0 ? TokenBucketCapacity.Value : fallbackCapacity;

    public int ResolveTokensPerSecond(int fallbackTokensPerSecond) =>
        TokensPerSecond is > 0 ? TokensPerSecond.Value : fallbackTokensPerSecond;
}
