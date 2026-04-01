namespace Lab.Shared.Configuration;

public sealed class LogPathOptions
{
    public string RequestsJsonl { get; set; } = "logs/requests.jsonl";

    public string JobsJsonl { get; set; } = "logs/jobs.jsonl";

    public string RunsDirectory { get; set; } = "logs/runs";

    public string AnalysisDirectory { get; set; } = "analysis";

    public string OperationalLogsDirectory { get; set; } = "logs";

    public string? StorefrontLog { get; set; }

    public string? CatalogLog { get; set; }

    public string? CartLog { get; set; }

    public string? OrderLog { get; set; }

    public string? PaymentSimulatorLog { get; set; }

    public string? ProxyLog { get; set; }

    public string? WorkerLog { get; set; }

    public string? SeedDataLog { get; set; }

    public string? LoadGenLog { get; set; }

    public string? AnalyzeLog { get; set; }
}
