using LoadGenTool.Workloads;

namespace LoadGenTool.Cli;

public sealed record LoadGenOptions(
    string TargetUrl,
    string Method,
    double RequestsPerSecond,
    TimeSpan Duration,
    int ConcurrencyCap,
    IReadOnlyDictionary<string, string> Headers,
    string? PayloadFile,
    string RunId,
    WorkloadMode Mode,
    int? BurstSize,
    TimeSpan BurstPeriod,
    bool ShowHelp)
{
    public static bool TryParse(
        IReadOnlyList<string> args,
        out LoadGenOptions options,
        out string? error)
    {
        string? targetUrl = null;
        string method = HttpMethod.Get.Method;
        double rps = 1d;
        TimeSpan duration = TimeSpan.FromSeconds(5);
        int concurrencyCap = 1;
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        string? payloadFile = null;
        string runId = $"loadgen-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}";
        WorkloadMode mode = WorkloadMode.Constant;
        int? burstSize = null;
        TimeSpan burstPeriod = TimeSpan.FromSeconds(1);
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

                case "--target-url":
                    if (!TryReadValue(args, ref index, out targetUrl, out error))
                    {
                        options = Empty();
                        return false;
                    }

                    break;

                case "--method":
                    if (!TryReadValue(args, ref index, out string? methodValue, out error))
                    {
                        options = Empty();
                        return false;
                    }

                    method = methodValue!.ToUpperInvariant();
                    break;

                case "--rps":
                    if (!TryReadDouble(args, ref index, out rps, out error))
                    {
                        options = Empty();
                        return false;
                    }

                    break;

                case "--duration-seconds":
                    if (!TryReadDouble(args, ref index, out double seconds, out error))
                    {
                        options = Empty();
                        return false;
                    }

                    duration = TimeSpan.FromSeconds(seconds);
                    break;

                case "--concurrency-cap":
                    if (!TryReadInt(args, ref index, out concurrencyCap, out error))
                    {
                        options = Empty();
                        return false;
                    }

                    break;

                case "--header":
                    if (!TryReadValue(args, ref index, out string? header, out error))
                    {
                        options = Empty();
                        return false;
                    }

                    string[] parts = header!.Split('=', 2, StringSplitOptions.TrimEntries);
                    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
                    {
                        error = $"Header '{header}' must be in Key=Value format.";
                        options = Empty();
                        return false;
                    }

                    headers[parts[0]] = parts[1];
                    break;

                case "--payload-file":
                    if (!TryReadValue(args, ref index, out payloadFile, out error))
                    {
                        options = Empty();
                        return false;
                    }

                    break;

                case "--run-id":
                    if (!TryReadValue(args, ref index, out string? parsedRunId, out error))
                    {
                        options = Empty();
                        return false;
                    }

                    runId = parsedRunId!;
                    break;

                case "--mode":
                    if (!TryReadValue(args, ref index, out string? modeValue, out error))
                    {
                        options = Empty();
                        return false;
                    }

                    if (!Enum.TryParse(modeValue, ignoreCase: true, out mode))
                    {
                        error = $"Unknown mode '{modeValue}'. Use 'constant' or 'burst'.";
                        options = Empty();
                        return false;
                    }

                    break;

                case "--burst-size":
                    if (!TryReadInt(args, ref index, out int parsedBurstSize, out error))
                    {
                        options = Empty();
                        return false;
                    }

                    burstSize = parsedBurstSize;
                    break;

                case "--burst-period-ms":
                    if (!TryReadInt(args, ref index, out int burstPeriodMs, out error))
                    {
                        options = Empty();
                        return false;
                    }

                    burstPeriod = TimeSpan.FromMilliseconds(burstPeriodMs);
                    break;

                default:
                    error = $"Unknown argument '{arg}'.";
                    options = Empty();
                    return false;
            }
        }

        if (!showHelp && string.IsNullOrWhiteSpace(targetUrl))
        {
            error = "The --target-url option is required.";
            options = Empty();
            return false;
        }

        if (!showHelp && !Uri.TryCreate(targetUrl, UriKind.Absolute, out _))
        {
            error = $"The target URL '{targetUrl}' is not a valid absolute URL.";
            options = Empty();
            return false;
        }

        if (rps <= 0d || duration <= TimeSpan.Zero || concurrencyCap <= 0)
        {
            error = "--rps, --duration-seconds, and --concurrency-cap must be positive.";
            options = Empty();
            return false;
        }

        if (burstSize.HasValue && burstSize.Value <= 0)
        {
            error = "--burst-size must be positive when supplied.";
            options = Empty();
            return false;
        }

        if (burstPeriod <= TimeSpan.Zero)
        {
            error = "--burst-period-ms must be positive.";
            options = Empty();
            return false;
        }

        if (!string.IsNullOrWhiteSpace(payloadFile) && !File.Exists(payloadFile))
        {
            error = $"The payload file '{payloadFile}' does not exist.";
            options = Empty();
            return false;
        }

        options = new LoadGenOptions(
            targetUrl ?? string.Empty,
            method,
            rps,
            duration,
            concurrencyCap,
            headers,
            payloadFile,
            runId,
            mode,
            burstSize,
            burstPeriod,
            showHelp);

        error = null;
        return true;
    }

    public static string GetUsage() =>
        """
        Usage:
          dotnet run --project src/LoadGen -- --target-url <url> [--method GET] [--rps 5] [--duration-seconds 10] [--concurrency-cap 4] [--header Key=Value] [--payload-file path] [--run-id run-123] [--mode constant|burst] [--burst-size 10] [--burst-period-ms 1000]

        Example:
          dotnet run --project src/LoadGen -- --target-url http://127.0.0.1:5081/ --rps 4 --duration-seconds 5 --concurrency-cap 2 --run-id run-123
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

    private static bool TryReadDouble(
        IReadOnlyList<string> args,
        ref int index,
        out double value,
        out string? error)
    {
        if (!TryReadValue(args, ref index, out string? raw, out error))
        {
            value = 0d;
            return false;
        }

        if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
        {
            error = $"Could not parse '{raw}' as a number.";
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

    private static LoadGenOptions Empty() => new(
        TargetUrl: string.Empty,
        Method: HttpMethod.Get.Method,
        RequestsPerSecond: 1d,
        Duration: TimeSpan.FromSeconds(1),
        ConcurrencyCap: 1,
        Headers: new Dictionary<string, string>(),
        PayloadFile: null,
        RunId: string.Empty,
        Mode: WorkloadMode.Constant,
        BurstSize: null,
        BurstPeriod: TimeSpan.FromSeconds(1),
        ShowHelp: false);
}
