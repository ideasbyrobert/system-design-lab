using Lab.Shared.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lab.Persistence.Checkout;
using Lab.Persistence.Projections;
using Lab.Persistence.Queueing;
using Lab.Persistence.Replication;

namespace Lab.Persistence.DependencyInjection;

public static class LabPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPrimaryPersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<PrimaryDbContextFactory>();
        services.TryAddSingleton<PrimaryDatabaseInitializer>();
        services.TryAddSingleton<Seeding.SqliteSeedDataService>();
        services.TryAddSingleton<ReplicaSyncService>();
        services.TryAddScoped<CheckoutPersistenceService>();
        services.TryAddScoped<IDurableQueueStore, SqliteDurableQueueStore>();

        services.AddDbContext<PrimaryDbContext>((serviceProvider, builder) =>
        {
            EnvironmentLayout layout = serviceProvider.GetRequiredService<EnvironmentLayout>();
            PrimaryDbContextFactory.Configure(builder, layout.PrimaryDatabasePath);
        });

        return services;
    }

    public static IServiceCollection AddReadModelPersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<PrimaryDbContextFactory>();
        services.TryAddSingleton<PrimaryDatabaseInitializer>();
        services.TryAddSingleton<ReadModelDbContextFactory>();
        services.TryAddSingleton<ReadModelDatabaseInitializer>();
        services.TryAddSingleton<ProductPageProjectionRebuilder>();
        services.TryAddSingleton<OrderHistoryProjectionRebuilder>();

        services.AddDbContext<ReadModelDbContext>((serviceProvider, builder) =>
        {
            EnvironmentLayout layout = serviceProvider.GetRequiredService<EnvironmentLayout>();
            ReadModelDbContextFactory.Configure(builder, layout.ReadModelDatabasePath);
        });

        return services;
    }
}
