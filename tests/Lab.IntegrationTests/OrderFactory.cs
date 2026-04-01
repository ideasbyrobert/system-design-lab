using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Order.Api;

namespace Lab.IntegrationTests;

internal sealed class OrderFactory(
    string repositoryRoot,
    IReadOnlyDictionary<string, string?>? configurationOverrides = null,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<OrderApiEntryPoint>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(configuration =>
        {
            Dictionary<string, string?> values = new()
            {
                ["Lab:Repository:RootPath"] = repositoryRoot
            };

            if (configurationOverrides is not null)
            {
                foreach ((string key, string? value) in configurationOverrides)
                {
                    values[key] = value;
                }
            }

            configuration.AddInMemoryCollection(values);
        });

        if (configureServices is not null)
        {
            builder.ConfigureServices(configureServices);
        }
    }
}
