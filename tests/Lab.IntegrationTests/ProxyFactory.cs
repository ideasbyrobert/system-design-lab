using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Proxy;

namespace Lab.IntegrationTests;

internal sealed class ProxyFactory(
    string repositoryRoot,
    IReadOnlyDictionary<string, string?>? configurationOverrides = null) : WebApplicationFactory<ProxyApiEntryPoint>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
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
