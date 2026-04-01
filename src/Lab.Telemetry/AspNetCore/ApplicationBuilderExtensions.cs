using Microsoft.AspNetCore.Builder;

namespace Lab.Telemetry.AspNetCore;

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseRequestTracing(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<RequestTracingMiddleware>();
    }
}
