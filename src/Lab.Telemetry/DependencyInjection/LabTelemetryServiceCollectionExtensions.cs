using Lab.Shared.Configuration;
using Lab.Shared.IO;
using Lab.Telemetry.AspNetCore;
using Lab.Telemetry.Persistence;
using Lab.Telemetry.RequestTracing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lab.Telemetry.DependencyInjection;

public static class LabTelemetryServiceCollectionExtensions
{
    public static IServiceCollection AddLabTelemetry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IRequestTraceContextAccessor, RequestTraceContextAccessor>();
        services.TryAddSingleton<IRequestTraceFactory>(serviceProvider =>
        {
            EnvironmentLayout layout = serviceProvider.GetRequiredService<EnvironmentLayout>();
            TimeProvider timeProvider = serviceProvider.GetRequiredService<TimeProvider>();

            return new RequestTraceFactory(layout.ServiceName, layout.CurrentRegion, timeProvider);
        });

        return services;
    }

    public static IServiceCollection AddRequestTraceJsonlWriter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<SafeFileAppender>();
        services.AddSingleton<IRequestTraceWriter>(serviceProvider =>
        {
            EnvironmentLayout layout = serviceProvider.GetRequiredService<EnvironmentLayout>();

            if (!AllowsRequestTracePersistence(layout.ServiceName))
            {
                throw new InvalidOperationException(
                    $"Request trace persistence is reserved for request-handling services, but the current service is '{layout.ServiceName}'.");
            }

            return new JsonlRequestTraceWriter(
                layout.RequestsJsonlPath,
                serviceProvider.GetRequiredService<SafeFileAppender>(),
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JsonlRequestTraceWriter>>());
        });

        return services;
    }

    public static IServiceCollection AddJobTraceJsonlWriter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<SafeFileAppender>();
        services.AddSingleton<IJobTraceWriter>(serviceProvider =>
        {
            EnvironmentLayout layout = serviceProvider.GetRequiredService<EnvironmentLayout>();

            if (!string.Equals(layout.ServiceName, "Worker", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Job trace persistence is reserved for Worker, but the current service is '{layout.ServiceName}'.");
            }

            return new JsonlJobTraceWriter(
                layout.JobsJsonlPath,
                serviceProvider.GetRequiredService<SafeFileAppender>(),
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JsonlJobTraceWriter>>());
        });

        return services;
    }

    private static bool AllowsRequestTracePersistence(string serviceName) =>
        serviceName.EndsWith(".Api", StringComparison.Ordinal) ||
        string.Equals(serviceName, "Proxy", StringComparison.Ordinal);
}
