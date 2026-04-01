using Lab.Telemetry.RequestTracing;

namespace Lab.Telemetry.AspNetCore;

public interface IRequestTraceContextAccessor
{
    RequestTraceContext? Current { get; set; }
}
