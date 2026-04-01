namespace Proxy.Routing;

public sealed record ProxySessionKeyResolution(
    string? SessionKey,
    string Source);

public static class ProxySessionKeySources
{
    public const string Header = "header";
    public const string Cookie = "cookie";
    public const string Missing = "missing";
}
