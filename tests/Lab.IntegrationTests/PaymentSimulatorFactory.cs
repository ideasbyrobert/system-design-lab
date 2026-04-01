using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using PaymentSimulator.Api;

namespace Lab.IntegrationTests;

internal sealed class PaymentSimulatorFactory(
    string repositoryRoot,
    IReadOnlyDictionary<string, string?>? configurationOverrides = null) : WebApplicationFactory<PaymentSimulatorApiEntryPoint>
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
    }
}
