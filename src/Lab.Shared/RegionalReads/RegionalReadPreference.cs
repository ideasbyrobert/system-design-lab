using Lab.Shared.Configuration;

namespace Lab.Shared.RegionalReads;

public sealed record RegionalReadPreference(
    string RequestedReadSource,
    string EffectiveReadSource,
    string TargetRegion,
    string SelectionScope,
    bool FallbackApplied,
    string? FallbackReason);

public static class RegionalReadPreferenceMetadata
{
    public static IReadOnlyDictionary<string, string?> Create(
        RegionalReadPreference preference,
        IReadOnlyDictionary<string, string?>? additionalMetadata = null)
    {
        ArgumentNullException.ThrowIfNull(preference);

        Dictionary<string, string?> metadata = new(StringComparer.Ordinal)
        {
            ["requestedReadSource"] = preference.RequestedReadSource,
            ["effectiveReadSource"] = preference.EffectiveReadSource,
            ["readSource"] = preference.EffectiveReadSource,
            ["targetRegion"] = preference.TargetRegion,
            ["selectionScope"] = preference.SelectionScope,
            ["fallbackApplied"] = preference.FallbackApplied.ToString().ToLowerInvariant(),
            ["fallbackReason"] = preference.FallbackReason
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

public static class RegionalProductReadPreferenceResolver
{
    public static RegionalReadPreference Resolve(
        string currentRegion,
        RegionOptions regionOptions,
        string? requestedReadSource,
        bool localReplicaAvailable = true)
    {
        ArgumentNullException.ThrowIfNull(regionOptions);

        string normalizedCurrentRegion = Normalize(currentRegion) ?? "local";
        string normalizedPrimaryRegion = Normalize(regionOptions.PrimaryRegion) ?? normalizedCurrentRegion;
        string normalizedEastRegion = Normalize(regionOptions.EastReplicaRegion) ?? "us-east";
        string normalizedWestRegion = Normalize(regionOptions.WestReplicaRegion) ?? "us-west";
        string normalizedRequestedReadSource = Normalize(requestedReadSource) ?? "local";

        return normalizedRequestedReadSource switch
        {
            "primary" => CreatePreference(normalizedRequestedReadSource, "primary", normalizedCurrentRegion, normalizedPrimaryRegion),
            "replica-east" => CreatePreference(normalizedRequestedReadSource, "replica-east", normalizedCurrentRegion, normalizedEastRegion),
            "replica-west" => CreatePreference(normalizedRequestedReadSource, "replica-west", normalizedCurrentRegion, normalizedWestRegion),
            "local" => ResolveLocalProductRead(
                normalizedCurrentRegion,
                normalizedPrimaryRegion,
                normalizedEastRegion,
                normalizedWestRegion,
                localReplicaAvailable),
            _ => throw new ArgumentOutOfRangeException(nameof(requestedReadSource), requestedReadSource, "Unknown regional product read source.")
        };
    }

    private static RegionalReadPreference ResolveLocalProductRead(
        string currentRegion,
        string primaryRegion,
        string eastRegion,
        string westRegion,
        bool localReplicaAvailable)
    {
        if (string.Equals(currentRegion, primaryRegion, StringComparison.OrdinalIgnoreCase))
        {
            return CreatePreference("local", "primary", currentRegion, primaryRegion);
        }

        if (string.Equals(currentRegion, eastRegion, StringComparison.OrdinalIgnoreCase))
        {
            if (!localReplicaAvailable)
            {
                RegionalReadPreference replicaUnavailableFallback = CreatePreference("local", "primary", currentRegion, primaryRegion);
                return replicaUnavailableFallback with
                {
                    FallbackApplied = true,
                    FallbackReason = "local_replica_unavailable"
                };
            }

            return CreatePreference("local", "replica-east", currentRegion, eastRegion);
        }

        if (string.Equals(currentRegion, westRegion, StringComparison.OrdinalIgnoreCase))
        {
            if (!localReplicaAvailable)
            {
                RegionalReadPreference replicaUnavailableFallback = CreatePreference("local", "primary", currentRegion, primaryRegion);
                return replicaUnavailableFallback with
                {
                    FallbackApplied = true,
                    FallbackReason = "local_replica_unavailable"
                };
            }

            return CreatePreference("local", "replica-west", currentRegion, westRegion);
        }

        RegionalReadPreference fallback = CreatePreference("local", "primary", currentRegion, primaryRegion);
        return fallback with
        {
            FallbackApplied = true,
            FallbackReason = "no_local_product_read_source_for_region"
        };
    }

    private static RegionalReadPreference CreatePreference(
        string requestedReadSource,
        string effectiveReadSource,
        string currentRegion,
        string targetRegion)
    {
        string selectionScope = string.Equals(currentRegion, targetRegion, StringComparison.OrdinalIgnoreCase)
            ? "same-region"
            : "cross-region";

        return new RegionalReadPreference(
            RequestedReadSource: requestedReadSource,
            EffectiveReadSource: effectiveReadSource,
            TargetRegion: targetRegion,
            SelectionScope: selectionScope,
            FallbackApplied: false,
            FallbackReason: null);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}

public static class RegionalOrderHistoryReadPreferenceResolver
{
    public static RegionalReadPreference Resolve(
        string currentRegion,
        RegionOptions regionOptions,
        string? requestedReadSource)
    {
        ArgumentNullException.ThrowIfNull(regionOptions);

        string normalizedCurrentRegion = Normalize(currentRegion) ?? "local";
        string normalizedPrimaryRegion = Normalize(regionOptions.PrimaryRegion) ?? normalizedCurrentRegion;
        string normalizedRequestedReadSource = Normalize(requestedReadSource) ?? "local";

        return normalizedRequestedReadSource switch
        {
            "read-model" => CreatePreference(normalizedRequestedReadSource, "read-model", normalizedCurrentRegion, normalizedCurrentRegion),
            "primary-projection" => CreatePreference(normalizedRequestedReadSource, "primary-projection", normalizedCurrentRegion, normalizedPrimaryRegion),
            "local" => CreatePreference(normalizedRequestedReadSource, "read-model", normalizedCurrentRegion, normalizedCurrentRegion),
            _ => throw new ArgumentOutOfRangeException(nameof(requestedReadSource), requestedReadSource, "Unknown order-history read source.")
        };
    }

    public static RegionalReadPreference CreatePrimaryProjectionFallback(
        string currentRegion,
        RegionOptions regionOptions,
        string requestedReadSource,
        string fallbackReason)
    {
        ArgumentNullException.ThrowIfNull(regionOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedReadSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackReason);

        string normalizedCurrentRegion = Normalize(currentRegion) ?? "local";
        string normalizedPrimaryRegion = Normalize(regionOptions.PrimaryRegion) ?? normalizedCurrentRegion;
        RegionalReadPreference preference = CreatePreference(
            Normalize(requestedReadSource) ?? "local",
            "primary-projection",
            normalizedCurrentRegion,
            normalizedPrimaryRegion);

        return preference with
        {
            FallbackApplied = true,
            FallbackReason = Normalize(fallbackReason)
        };
    }

    private static RegionalReadPreference CreatePreference(
        string requestedReadSource,
        string effectiveReadSource,
        string currentRegion,
        string targetRegion)
    {
        string selectionScope = string.Equals(currentRegion, targetRegion, StringComparison.OrdinalIgnoreCase)
            ? "same-region"
            : "cross-region";

        return new RegionalReadPreference(
            RequestedReadSource: requestedReadSource,
            EffectiveReadSource: effectiveReadSource,
            TargetRegion: targetRegion,
            SelectionScope: selectionScope,
            FallbackApplied: false,
            FallbackReason: null);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
