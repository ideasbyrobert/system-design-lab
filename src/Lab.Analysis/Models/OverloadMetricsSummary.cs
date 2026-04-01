namespace Lab.Analysis.Models;

public sealed record OverloadMetricsSummary
{
    public int RejectedRequestCount { get; init; }

    public double? RejectFraction { get; init; }

    public int TimeoutRequestCount { get; init; }

    public double? TimeoutFraction { get; init; }

    public int AdmittedRequestCount { get; init; }

    public double? AdmittedFraction { get; init; }

    public int RetriedJobCount { get; init; }

    public int TotalRetryAttempts { get; init; }

    public RequestCohortMetricsSummary RateLimitedRequests { get; init; } = new();

    public RequestCohortMetricsSummary AdmittedRequests { get; init; } = new();
}
