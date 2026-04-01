using Lab.Telemetry.RequestTracing;

namespace Lab.Telemetry.Persistence;

public interface IRequestTraceWriter
{
    ValueTask<bool> WriteAsync(RequestTraceRecord traceRecord, CancellationToken cancellationToken = default);
}
