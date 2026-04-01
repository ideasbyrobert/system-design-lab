namespace Lab.Shared.Caching;

public sealed record CacheScope
{
    public required string NamespaceName { get; init; }

    public required string Region { get; init; }

    public static CacheScope Create(string namespaceName, string region)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            throw new ArgumentException("A cache namespace is required.", nameof(namespaceName));
        }

        if (string.IsNullOrWhiteSpace(region))
        {
            throw new ArgumentException("A cache region is required.", nameof(region));
        }

        return new CacheScope
        {
            NamespaceName = namespaceName.Trim(),
            Region = region.Trim()
        };
    }
}
