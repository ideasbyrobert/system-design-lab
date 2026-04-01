using Lab.Shared.Configuration;
using Lab.Telemetry.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Worker.Jobs;

namespace Worker.DependencyInjection;

public static class WorkerServiceCollectionExtensions
{
    public static IServiceCollection AddLabWorkerProcessing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddJobTraceJsonlWriter();
        services.AddHttpClient<IPaymentConfirmationClient, HttpPaymentConfirmationClient>((serviceProvider, httpClient) =>
        {
            ServiceEndpointOptions options = serviceProvider.GetRequiredService<IOptions<ServiceEndpointOptions>>().Value;
            httpClient.BaseAddress = new Uri(options.PaymentSimulatorBaseUrl, UriKind.Absolute);
        });
        services.AddScoped<IWorkerJobHandler, PaymentConfirmationRetryJobHandler>();
        services.AddScoped<IWorkerJobHandler, OrderHistoryProjectionUpdateJobHandler>();
        services.AddScoped<IWorkerJobHandler, ProductPageProjectionRebuildJobHandler>();
        services.AddScoped<WorkerJobDispatcher>();
        services.AddSingleton<WorkerQueueProcessor>();
        services.AddHostedService<BackgroundWorker>();

        return services;
    }
}
