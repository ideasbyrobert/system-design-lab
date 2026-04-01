using Microsoft.AspNetCore.Http;

namespace Proxy.Routing;

public sealed record ProxyRouteTarget(
    string Name,
    PathString PathPrefix,
    IReadOnlyList<Uri> Backends)
{
    public bool Matches(PathString requestPath)
    {
        if (PathPrefix == "/")
        {
            return true;
        }

        return requestPath.StartsWithSegments(PathPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
