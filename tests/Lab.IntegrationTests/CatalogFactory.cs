using Catalog.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Lab.IntegrationTests;

internal sealed class CatalogFactory(
    string repositoryRoot,
    IReadOnlyDictionary<string, string?>? configurationOverrides = null) : WebApplicationFactory<CatalogApiEntryPoint>
{
    protected override IHost CreateHost(IHostBuilder builder)
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

        return base.CreateHost(builder);
    }
}
