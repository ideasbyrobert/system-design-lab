using Lab.Shared.Configuration;
using Lab.Shared.RegionalReads;

namespace Catalog.Api.ProductDetails;

internal enum CatalogProductReadSource
{
    Local,
    Primary,
    ReplicaEast,
    ReplicaWest
}

internal static class CatalogProductReadSourceParser
{
    public static bool TryParse(string? value, out CatalogProductReadSource readSource, out string? validationError)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            readSource = CatalogProductReadSource.Local;
            validationError = null;
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "local":
                readSource = CatalogProductReadSource.Local;
                validationError = null;
                return true;

            case "primary":
                readSource = CatalogProductReadSource.Primary;
                validationError = null;
                return true;

            case "replica-east":
                readSource = CatalogProductReadSource.ReplicaEast;
                validationError = null;
                return true;

            case "replica-west":
                readSource = CatalogProductReadSource.ReplicaWest;
                validationError = null;
                return true;

            default:
                readSource = CatalogProductReadSource.Local;
                validationError = "Read source must be 'local', 'primary', 'replica-east', or 'replica-west'.";
                return false;
        }
    }

    public static string ToText(this CatalogProductReadSource readSource) =>
        readSource switch
        {
            CatalogProductReadSource.Local => "local",
            CatalogProductReadSource.Primary => "primary",
            CatalogProductReadSource.ReplicaEast => "replica-east",
            CatalogProductReadSource.ReplicaWest => "replica-west",
            _ => throw new ArgumentOutOfRangeException(nameof(readSource), readSource, "Unknown catalog product read source.")
        };
}

internal sealed record CatalogProductReadTarget(
    CatalogProductReadSource ReadSource,
    string RequestedReadSourceText,
    string ReadSourceText,
    string DatabasePath,
    string DatabaseLabel,
    string TargetRegion,
    string SelectionScope,
    bool FallbackApplied,
    string? FallbackReason);

internal static class CatalogProductReadTargetResolver
{
    public static CatalogProductReadTarget Resolve(
        EnvironmentLayout layout,
        RegionOptions regionOptions,
        RegionalDegradationOptions degradationOptions,
        CatalogProductReadSource readSource)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(regionOptions);
        ArgumentNullException.ThrowIfNull(degradationOptions);

        RegionalReadPreference preference = RegionalProductReadPreferenceResolver.Resolve(
            layout.CurrentRegion,
            regionOptions,
            readSource.ToText(),
            localReplicaAvailable: !degradationOptions.SimulateLocalReplicaUnavailable);

        return preference.EffectiveReadSource switch
        {
            "primary" => new CatalogProductReadTarget(
                ReadSource: CatalogProductReadSource.Primary,
                RequestedReadSourceText: preference.RequestedReadSource,
                ReadSourceText: preference.EffectiveReadSource,
                DatabasePath: layout.PrimaryDatabasePath,
                DatabaseLabel: Path.GetFileName(layout.PrimaryDatabasePath),
                TargetRegion: preference.TargetRegion,
                SelectionScope: preference.SelectionScope,
                FallbackApplied: preference.FallbackApplied,
                FallbackReason: preference.FallbackReason),
            "replica-east" => new CatalogProductReadTarget(
                ReadSource: CatalogProductReadSource.ReplicaEast,
                RequestedReadSourceText: preference.RequestedReadSource,
                ReadSourceText: preference.EffectiveReadSource,
                DatabasePath: layout.ReplicaEastDatabasePath,
                DatabaseLabel: Path.GetFileName(layout.ReplicaEastDatabasePath),
                TargetRegion: preference.TargetRegion,
                SelectionScope: preference.SelectionScope,
                FallbackApplied: preference.FallbackApplied,
                FallbackReason: preference.FallbackReason),
            "replica-west" => new CatalogProductReadTarget(
                ReadSource: CatalogProductReadSource.ReplicaWest,
                RequestedReadSourceText: preference.RequestedReadSource,
                ReadSourceText: preference.EffectiveReadSource,
                DatabasePath: layout.ReplicaWestDatabasePath,
                DatabaseLabel: Path.GetFileName(layout.ReplicaWestDatabasePath),
                TargetRegion: preference.TargetRegion,
                SelectionScope: preference.SelectionScope,
                FallbackApplied: preference.FallbackApplied,
                FallbackReason: preference.FallbackReason),
            _ => throw new ArgumentOutOfRangeException(nameof(readSource), readSource, "Unknown catalog product read source.")
        };
    }
}
