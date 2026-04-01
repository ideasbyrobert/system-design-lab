using Lab.Telemetry.RequestTracing;

namespace Lab.Telemetry.AspNetCore;

internal sealed class RequestTraceContextAccessor : IRequestTraceContextAccessor
{
    public RequestTraceContext? Current { get; set; }
}
