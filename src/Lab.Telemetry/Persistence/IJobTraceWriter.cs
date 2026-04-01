using Lab.Telemetry.RequestTracing;

namespace Lab.Telemetry.Persistence;

public interface IJobTraceWriter
{
    ValueTask<bool> WriteAsync(JobTraceRecord traceRecord, CancellationToken cancellationToken = default);
}
