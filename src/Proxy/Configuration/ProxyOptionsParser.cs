using Microsoft.Extensions.Configuration;

namespace Proxy.Configuration;

public static class ProxyOptionsParser
{
    public static void Apply(ProxyOptions options, IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(section);

        options.RoutingMode = ProxyRoutingModes.Normalize(
            section.GetValue<string>(nameof(ProxyOptions.RoutingMode)));
        options.Storefront = ParseGroup(section.GetSection(nameof(ProxyOptions.Storefront)), "/");
        options.Catalog = ParseGroup(section.GetSection(nameof(ProxyOptions.Catalog)), "/catalog");
    }

    private static ProxyBackendGroupOptions ParseGroup(IConfigurationSection section, string defaultPathPrefix)
    {
        return new ProxyBackendGroupOptions
        {
            Enabled = section.GetValue<bool?>(nameof(ProxyBackendGroupOptions.Enabled)) ?? true,
            PathPrefix = section.GetValue<string>(nameof(ProxyBackendGroupOptions.PathPrefix)) ?? defaultPathPrefix,
            Backends = ReadIndexedBackends(section.GetSection(nameof(ProxyBackendGroupOptions.Backends)))
        };
    }

    private static List<string> ReadIndexedBackends(IConfigurationSection section)
    {
        List<string> backends = [];

        for (int index = 0; ; index++)
        {
            string? value = section[index.ToString()];
            if (string.IsNullOrWhiteSpace(value))
            {
                break;
            }

            backends.Add(value.Trim());
        }

        return backends;
    }
}
