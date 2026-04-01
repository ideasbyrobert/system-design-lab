namespace Lab.Shared.Configuration;

public sealed class CacheOptions
{
    public bool Enabled { get; set; } = true;

    public int DefaultTtlSeconds { get; set; } = 60;

    public int Capacity { get; set; } = 1024;
}
