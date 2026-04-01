using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lab.Shared.RateLimiting;

public static class LabRateLimitingServiceCollectionExtensions
{
    public static IServiceCollection AddLabTokenBucketRateLimiting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITokenBucketRateLimiter, InMemoryTokenBucketRateLimiter>();

        return services;
    }
}
