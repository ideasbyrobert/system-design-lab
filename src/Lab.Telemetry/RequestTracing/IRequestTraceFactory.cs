using Lab.Shared.Contracts;

namespace Lab.Telemetry.RequestTracing;

public interface IRequestTraceFactory
{
    RequestTraceContext BeginRequest(
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
        string? spanId = null);
}
