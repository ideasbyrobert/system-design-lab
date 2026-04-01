namespace Proxy.Configuration;

public sealed class ProxyOptions
{
    public string RoutingMode { get; set; } = ProxyRoutingModes.RoundRobin;

    public ProxyBackendGroupOptions Storefront { get; set; } = new()
    {
        PathPrefix = "/"
    };

    public ProxyBackendGroupOptions Catalog { get; set; } = new()
    {
        PathPrefix = "/catalog"
    };
}

public sealed class ProxyBackendGroupOptions
{
    public bool Enabled { get; set; } = true;

    public string PathPrefix { get; set; } = "/";

    public List<string> Backends { get; set; } = [];
}

public static class ProxyRoutingModes
{
    public const string RoundRobin = "round_robin";
    public const string Sticky = "sticky";

    public static string Normalize(string? value)
    {
        if (string.Equals(value, Sticky, StringComparison.OrdinalIgnoreCase))
        {
            return Sticky;
        }

        return RoundRobin;
    }
}
