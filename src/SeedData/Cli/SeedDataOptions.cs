namespace SeedDataTool.Cli;

public sealed record SeedDataOptions(
    int ProductCount,
    int UserCount,
    bool ResetExisting,
    bool RebuildProductPageProjection,
    bool SkipPrimarySeed,
    bool SyncReplicas,
    int? ReplicaEastLagMillisecondsOverride,
    int? ReplicaWestLagMillisecondsOverride,
    bool ShowHelp)
{
    public static SeedDataOptions Default { get; } = new(
        ProductCount: 50,
        UserCount: 10,
        ResetExisting: true,
        RebuildProductPageProjection: false,
        SkipPrimarySeed: false,
        SyncReplicas: false,
        ReplicaEastLagMillisecondsOverride: null,
        ReplicaWestLagMillisecondsOverride: null,
        ShowHelp: false);

    public static bool TryParse(
        IReadOnlyList<string> args,
        out SeedDataOptions options,
        out string? error)
    {
        int products = Default.ProductCount;
        int users = Default.UserCount;
        bool reset = Default.ResetExisting;
        bool rebuildProductPageProjection = Default.RebuildProductPageProjection;
        bool skipPrimarySeed = Default.SkipPrimarySeed;
        bool syncReplicas = Default.SyncReplicas;
        int? replicaEastLagMillisecondsOverride = Default.ReplicaEastLagMillisecondsOverride;
        int? replicaWestLagMillisecondsOverride = Default.ReplicaWestLagMillisecondsOverride;
        bool showHelp = false;

        for (int index = 0; index < args.Count; index++)
        {
            string arg = args[index];

            switch (arg)
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;

                case "--products":
                    if (!TryReadInt(args, ref index, out products, out error))
                    {
                        options = Default;
                        return false;
                    }

                    break;

                case "--users":
                    if (!TryReadInt(args, ref index, out users, out error))
                    {
                        options = Default;
                        return false;
                    }

                    break;

                case "--reset":
                    if (!TryReadBool(args, ref index, out reset, out error))
                    {
                        options = Default;
                        return false;
                    }

                    break;

                case "--rebuild-product-page-projection":
                    if (!TryReadBool(args, ref index, out rebuildProductPageProjection, out error))
                    {
                        options = Default;
                        return false;
                    }

                    break;

                case "--skip-primary-seed":
                    if (!TryReadBool(args, ref index, out skipPrimarySeed, out error))
                    {
                        options = Default;
                        return false;
                    }

                    break;

                case "--sync-replicas":
                    if (!TryReadBool(args, ref index, out syncReplicas, out error))
                    {
                        options = Default;
                        return false;
                    }

                    break;

                case "--replica-east-lag-ms":
                    if (!TryReadInt(args, ref index, out int replicaEastLagMs, out error))
                    {
                        options = Default;
                        return false;
                    }

                    replicaEastLagMillisecondsOverride = replicaEastLagMs;
                    break;

                case "--replica-west-lag-ms":
                    if (!TryReadInt(args, ref index, out int replicaWestLagMs, out error))
                    {
                        options = Default;
                        return false;
                    }

                    replicaWestLagMillisecondsOverride = replicaWestLagMs;
                    break;

                default:
                    error = $"Unknown argument '{arg}'.";
                    options = Default;
                    return false;
            }
        }

        if (!skipPrimarySeed && (products <= 0 || users <= 0))
        {
            error = "Both --products and --users must be positive.";
            options = Default;
            return false;
        }

        if (skipPrimarySeed && !rebuildProductPageProjection && !syncReplicas)
        {
            error = "When --skip-primary-seed is true, either --rebuild-product-page-projection or --sync-replicas must also be true.";
            options = Default;
            return false;
        }

        if ((replicaEastLagMillisecondsOverride.HasValue && replicaEastLagMillisecondsOverride.Value < 0) ||
            (replicaWestLagMillisecondsOverride.HasValue && replicaWestLagMillisecondsOverride.Value < 0))
        {
            error = "Replica lag overrides must be non-negative.";
            options = Default;
            return false;
        }

        if ((replicaEastLagMillisecondsOverride.HasValue || replicaWestLagMillisecondsOverride.HasValue) && !syncReplicas)
        {
            error = "Replica lag overrides require --sync-replicas true.";
            options = Default;
            return false;
        }

        options = new SeedDataOptions(
            products,
            users,
            reset,
            rebuildProductPageProjection,
            skipPrimarySeed,
            syncReplicas,
            replicaEastLagMillisecondsOverride,
            replicaWestLagMillisecondsOverride,
            showHelp);
        error = null;
        return true;
    }

    public static string GetUsage() =>
        """
        Usage:
          dotnet run --project src/SeedData -- [--products <count>] [--users <count>] [--reset <true|false>] [--rebuild-product-page-projection <true|false>] [--skip-primary-seed <true|false>] [--sync-replicas <true|false>] [--replica-east-lag-ms <milliseconds>] [--replica-west-lag-ms <milliseconds>]

        Example:
          dotnet run --project src/SeedData -- --products 100 --users 25 --reset true --rebuild-product-page-projection true --sync-replicas true --replica-east-lag-ms 250
        """;

    private static bool TryReadInt(
        IReadOnlyList<string> args,
        ref int index,
        out int value,
        out string? error)
    {
        if (!TryReadValue(args, ref index, out string? raw, out error))
        {
            value = 0;
            return false;
        }

        if (!int.TryParse(raw, out value))
        {
            error = $"Could not parse '{raw}' as an integer.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryReadBool(
        IReadOnlyList<string> args,
        ref int index,
        out bool value,
        out string? error)
    {
        if (!TryReadValue(args, ref index, out string? raw, out error))
        {
            value = false;
            return false;
        }

        if (!bool.TryParse(raw, out value))
        {
            error = $"Could not parse '{raw}' as a boolean.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryReadValue(
        IReadOnlyList<string> args,
        ref int index,
        out string? value,
        out string? error)
    {
        if (index + 1 >= args.Count)
        {
            value = null;
            error = $"Missing value after '{args[index]}'.";
            return false;
        }

        value = args[++index];
        error = null;
        return true;
    }
}
