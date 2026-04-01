namespace Lab.Shared.Networking;

public static class DependencyCallNetworkMetadata
{
    public static IReadOnlyDictionary<string, string?> Create(
        RegionNetworkEnvelope envelope,
        IReadOnlyDictionary<string, string?>? additionalMetadata = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        Dictionary<string, string?> metadata = new(StringComparer.Ordinal)
        {
            ["callerRegion"] = envelope.CallerRegion,
            ["targetRegion"] = envelope.TargetRegion,
            ["networkScope"] = envelope.NetworkScope,
            ["injectedDelayMs"] = envelope.InjectedDelayMs.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        if (additionalMetadata is not null)
        {
            foreach ((string key, string? value) in additionalMetadata)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    metadata[key.Trim()] = value;
                }
            }
        }

        return metadata;
    }
}
