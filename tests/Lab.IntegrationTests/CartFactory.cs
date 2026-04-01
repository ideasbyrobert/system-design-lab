using Cart.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Lab.IntegrationTests;

internal sealed class CartFactory(string repositoryRoot) : WebApplicationFactory<CartApiEntryPoint>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lab:Repository:RootPath"] = repositoryRoot
            });
        });

        return base.CreateHost(builder);
    }
}
