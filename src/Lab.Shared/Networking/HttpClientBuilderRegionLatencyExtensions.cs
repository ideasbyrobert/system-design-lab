using Lab.Shared.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lab.Shared.Networking;

public static class HttpClientBuilderRegionLatencyExtensions
{
    public static IHttpClientBuilder AddRegionLatencyInjection(
        this IHttpClientBuilder builder,
        string dependencyName,
        Func<IServiceProvider, string?> targetRegionAccessor)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(dependencyName);
        ArgumentNullException.ThrowIfNull(targetRegionAccessor);

        return builder.AddHttpMessageHandler(serviceProvider =>
            new RegionLatencyInjectionDelegatingHandler(
                serviceProvider.GetRequiredService<IRegionNetworkEnvelopePolicy>(),
                serviceProvider.GetRequiredService<ILogger<RegionLatencyInjectionDelegatingHandler>>(),
                dependencyName.Trim(),
                targetRegionAccessor(serviceProvider)));
    }
}
