using Lab.Shared.Contracts;

namespace Lab.Telemetry.RequestTracing;

public sealed class RequestTraceFactory : IRequestTraceFactory
{
    private readonly string _service;
    private readonly string _region;
    private readonly TimeProvider _timeProvider;

    public RequestTraceFactory(string service, string region, TimeProvider? timeProvider = null, string? runId = null)
    {
        _service = RequireText(service, nameof(service));
        _region = RequireText(region, nameof(region));
        _timeProvider = timeProvider ?? TimeProvider.System;
        RunId = string.IsNullOrWhiteSpace(runId)
            ? CreateRunId(_service, _timeProvider.GetUtcNow())
            : runId.Trim();
    }

    public string RunId { get; }

    public RequestTraceContext BeginRequest(
        OperationContractDescriptor contract,
        string route,
        string method,
        string requestId,
        string? runId = null,
        string? userId = null,
        string? correlationId = null,
        DateTimeOffset? arrivalUtc = null,
        long? arrivalTimestamp = null,
        DateTimeOffset? startUtc = null,
        long? startTimestamp = null,
        string? traceId = null,
        string? spanId = null)
    {
        ArgumentNullException.ThrowIfNull(contract);

        ValidateTimePair(arrivalUtc, arrivalTimestamp, nameof(arrivalUtc), nameof(arrivalTimestamp));
        ValidateTimePair(startUtc, startTimestamp, nameof(startUtc), nameof(startTimestamp));

        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        long nowTimestamp = _timeProvider.GetTimestamp();

        DateTimeOffset effectiveArrivalUtc = arrivalUtc ?? nowUtc;
        long effectiveArrivalTimestamp = arrivalTimestamp ?? nowTimestamp;
        DateTimeOffset effectiveStartUtc = startUtc ?? effectiveArrivalUtc;
        long effectiveStartTimestamp = startTimestamp ?? effectiveArrivalTimestamp;

        return new RequestTraceContext(
            timeProvider: _timeProvider,
            runId: string.IsNullOrWhiteSpace(runId) ? RunId : runId.Trim(),
            traceId: string.IsNullOrWhiteSpace(traceId) ? CreateTraceId() : traceId.Trim(),
            spanId: string.IsNullOrWhiteSpace(spanId) ? CreateSpanId() : spanId.Trim(),
            requestId: RequireText(requestId, nameof(requestId)),
            service: _service,
            region: _region,
            route: RequireText(route, nameof(route)),
            method: RequireText(method, nameof(method)).ToUpperInvariant(),
            contract: contract,
            arrivalUtc: effectiveArrivalUtc,
            arrivalTimestamp: effectiveArrivalTimestamp,
            startUtc: effectiveStartUtc,
            startTimestamp: effectiveStartTimestamp,
            userId: NormalizeOptionalText(userId),
            correlationId: NormalizeOptionalText(correlationId));
    }

    private static void ValidateTimePair(
        DateTimeOffset? utcValue,
        long? timestampValue,
        string utcName,
        string timestampName)
    {
        bool utcSupplied = utcValue.HasValue;
        bool timestampSupplied = timestampValue.HasValue;

        if (utcSupplied == timestampSupplied)
        {
            return;
        }

        throw new ArgumentException($"Supply {utcName} and {timestampName} together to keep elapsed-time measurement monotonic.");
    }

    private static string CreateRunId(string service, DateTimeOffset nowUtc)
    {
        string normalizedService = service.ToLowerInvariant();
        return $"{normalizedService}-{nowUtc:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
    }

    private static string CreateTraceId() => Guid.NewGuid().ToString("N");

    private static string CreateSpanId() => Guid.NewGuid().ToString("N")[..16];

    private static string RequireText(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", paramName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
