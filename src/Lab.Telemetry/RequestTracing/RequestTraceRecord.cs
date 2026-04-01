namespace Lab.Telemetry.RequestTracing;

public sealed record class RequestTraceRecord
{
    public required string RunId { get; init; }

    public required string TraceId { get; init; }

    public required string SpanId { get; init; }

    public required string RequestId { get; init; }

    public required string Operation { get; init; }

    public required string Region { get; init; }

    public required string Service { get; init; }

    public required string Route { get; init; }

    public required string Method { get; init; }

    public required DateTimeOffset ArrivalUtc { get; init; }

    public required DateTimeOffset StartUtc { get; init; }

    public required DateTimeOffset CompletionUtc { get; init; }

    public required double LatencyMs { get; init; }

    public required int StatusCode { get; init; }

    public required bool ContractSatisfied { get; init; }

    public required bool CacheHit { get; init; }

    public required bool RateLimited { get; init; }

    public IReadOnlyList<DependencyCallRecord> DependencyCalls { get; init; } = Array.Empty<DependencyCallRecord>();

    public IReadOnlyList<StageTimingRecord> StageTimings { get; init; } = Array.Empty<StageTimingRecord>();

    public string? ErrorCode { get; init; }

    public string? UserId { get; init; }

    public string? SessionKey { get; init; }

    public string? CorrelationId { get; init; }

    public string? ReadSource { get; init; }

    public int? FreshnessComparedCount { get; init; }

    public int? FreshnessStaleCount { get; init; }

    public double? FreshnessStaleFraction { get; init; }

    public double? MaxStalenessAgeMs { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}
