using Lab.Shared.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Lab.Shared.Caching;

public static class CacheServiceCollectionExtensions
{
    public static IServiceCollection AddInMemoryLabCache(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<InMemoryCacheStore>(serviceProvider =>
        {
            CacheOptions options = serviceProvider.GetRequiredService<IOptions<CacheOptions>>().Value;
            TimeProvider timeProvider = serviceProvider.GetRequiredService<TimeProvider>();

            return new InMemoryCacheStore(
                timeProvider,
                TimeSpan.FromSeconds(Math.Max(options.DefaultTtlSeconds, 1)),
                Math.Max(options.Capacity, 1));
        });
        services.TryAddSingleton<ICacheStore>(serviceProvider => serviceProvider.GetRequiredService<InMemoryCacheStore>());
        services.TryAddSingleton<ICacheSnapshotProvider>(serviceProvider => serviceProvider.GetRequiredService<InMemoryCacheStore>());

        return services;
    }
}
