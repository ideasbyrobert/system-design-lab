using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Lab.Shared.Networking;

namespace Lab.Shared.Configuration;

public static class LabConfigurationServiceCollectionExtensions
{
    public static IServiceCollection AddLabConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        services.AddOptions<RepositoryOptions>()
            .Bind(configuration.GetSection(LabConfigurationSections.Repository));

        services.AddOptions<DatabasePathOptions>()
            .Bind(configuration.GetSection(LabConfigurationSections.DatabasePaths));

        services.AddOptions<LogPathOptions>()
            .Bind(configuration.GetSection(LabConfigurationSections.LogPaths));

        services.AddOptions<RegionOptions>()
            .Bind(configuration.GetSection(LabConfigurationSections.Regions));

        services.AddOptions<RegionalDegradationOptions>()
            .Bind(configuration.GetSection(LabConfigurationSections.RegionalDegradation));

        services.AddOptions<ServiceEndpointOptions>()
            .Bind(configuration.GetSection(LabConfigurationSections.ServiceEndpoints));

        services.AddOptions<CacheOptions>()
            .Bind(configuration.GetSection(LabConfigurationSections.Cache));

        services.AddOptions<QueueOptions>()
            .Bind(configuration.GetSection(LabConfigurationSections.Queue));

        services.AddOptions<ReplicaSyncOptions>()
            .Bind(configuration.GetSection(LabConfigurationSections.ReplicaSync));

        services.AddOptions<PaymentSimulatorOptions>()
            .Bind(configuration.GetSection(LabConfigurationSections.PaymentSimulator));

        services.AddOptions<RateLimiterOptions>()
            .Bind(configuration.GetSection(LabConfigurationSections.RateLimiter));

        services.AddSingleton(sp =>
            EnvironmentLayout.Create(
                hostEnvironment,
                sp.GetRequiredService<IOptions<RepositoryOptions>>().Value,
                sp.GetRequiredService<IOptions<DatabasePathOptions>>().Value,
                sp.GetRequiredService<IOptions<LogPathOptions>>().Value,
                sp.GetRequiredService<IOptions<RegionOptions>>().Value));

        services.AddSingleton<IRegionNetworkEnvelopePolicy, ConfiguredRegionNetworkEnvelopePolicy>();

        return services;
    }
}
