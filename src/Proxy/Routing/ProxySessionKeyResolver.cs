namespace Proxy.Routing;

public static class ProxySessionKeyResolver
{
    public static ProxySessionKeyResolution Resolve(string? sessionKeyHeader, string? sessionCookie)
    {
        string? normalizedHeader = Normalize(sessionKeyHeader);
        if (normalizedHeader is not null)
        {
            return new ProxySessionKeyResolution(normalizedHeader, ProxySessionKeySources.Header);
        }

        string? normalizedCookie = Normalize(sessionCookie);
        if (normalizedCookie is not null)
        {
            return new ProxySessionKeyResolution(normalizedCookie, ProxySessionKeySources.Cookie);
        }

        return new ProxySessionKeyResolution(null, ProxySessionKeySources.Missing);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
