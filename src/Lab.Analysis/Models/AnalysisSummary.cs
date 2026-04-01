namespace Lab.Analysis.Models;

public sealed record AnalysisSummary
{
    public required string RunId { get; init; }

    public required DateTimeOffset GeneratedUtc { get; init; }

    public required string RequestsPath { get; init; }

    public required string JobsPath { get; init; }

    public string? FilterRunId { get; init; }

    public DateTimeOffset? FilterFromUtc { get; init; }

    public DateTimeOffset? FilterToUtc { get; init; }

    public string? FilterOperation { get; init; }

    public IReadOnlyList<string> IncludedRunIds { get; init; } = Array.Empty<string>();

    public required QueueMetricsSummary Queue { get; init; }

    public required RequestMetricsSummary Requests { get; init; }

    public required ReadFreshnessMetricsSummary ReadFreshness { get; init; }

    public required OverloadMetricsSummary Overload { get; init; }

    public required JobMetricsSummary Jobs { get; init; }
}
