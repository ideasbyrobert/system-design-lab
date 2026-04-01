using Lab.Analysis.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lab.Analysis.DependencyInjection;

public static class LabAnalysisServiceCollectionExtensions
{
    public static IServiceCollection AddLabAnalysis(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<TelemetryJsonlReader>();
        services.TryAddSingleton<QueueMetricsReader>();
        services.TryAddSingleton<TelemetryAnalyzer>();
        services.TryAddSingleton<AnalysisArtifactWriter>();

        return services;
    }
}
