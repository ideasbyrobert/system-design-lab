using Lab.Analysis.Models;

namespace Lab.Analysis.Cli;

public sealed record AnalyzeCliOptions(
    string? RunId,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    string? Operation,
    bool ShowHelp)
{
    public AnalysisFilter ToFilter() => new(RunId, FromUtc, ToUtc, Operation);

    public static bool TryParse(
        IReadOnlyList<string> args,
        out AnalyzeCliOptions options,
        out string? error)
    {
        string? runId = null;
        DateTimeOffset? fromUtc = null;
        DateTimeOffset? toUtc = null;
        string? operation = null;
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

                case "--run-id":
                    if (!TryReadValue(args, ref index, out runId, out error))
                    {
                        options = Empty();
                        return false;
                    }

                    break;

                case "--from":
                    if (!TryReadDateTimeOffset(args, ref index, out fromUtc, out error))
                    {
                        options = Empty();
                        return false;
                    }

                    break;

                case "--to":
                    if (!TryReadDateTimeOffset(args, ref index, out toUtc, out error))
                    {
                        options = Empty();
                        return false;
                    }

                    break;

                case "--operation":
                    if (!TryReadValue(args, ref index, out operation, out error))
                    {
                        options = Empty();
                        return false;
                    }

                    break;

                default:
                    error = $"Unknown argument '{arg}'.";
                    options = Empty();
                    return false;
            }
        }

        if (fromUtc.HasValue && toUtc.HasValue && fromUtc > toUtc)
        {
            error = "The --from value must be earlier than or equal to --to.";
            options = Empty();
            return false;
        }

        options = new AnalyzeCliOptions(runId, fromUtc, toUtc, operation, showHelp);
        error = null;
        return true;
    }

    public static string GetUsage() =>
        """
        Usage:
          dotnet run --project src/Analyze -- [--run-id <run-id>] [--from <ISO-8601>] [--to <ISO-8601>] [--operation <operation>]

        Examples:
          dotnet run --project src/Analyze -- --run-id storefront.api-20260331T110557496Z-abc123
          dotnet run --project src/Analyze -- --run-id milestone-2-cache-on --operation product-page
          dotnet run --project src/Analyze -- --from 2026-03-31T11:00:00Z --to 2026-03-31T12:00:00Z
        """;

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

    private static bool TryReadDateTimeOffset(
        IReadOnlyList<string> args,
        ref int index,
        out DateTimeOffset? value,
        out string? error)
    {
        if (!TryReadValue(args, ref index, out string? raw, out error))
        {
            value = null;
            return false;
        }

        if (!DateTimeOffset.TryParse(raw, out DateTimeOffset parsed))
        {
            value = null;
            error = $"Could not parse '{raw}' as a date/time offset.";
            return false;
        }

        value = parsed;
        error = null;
        return true;
    }

    private static AnalyzeCliOptions Empty() => new(null, null, null, null, false);
}
