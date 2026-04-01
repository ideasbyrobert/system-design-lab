namespace Storefront.Api.Routing;

internal static class StorefrontSessionKeySources
{
    public const string Header = "header";

    public const string Cookie = "cookie";

    public const string Generated = "generated";
}

internal sealed record StorefrontSessionKeyResolution(
    string SessionKey,
    string Source,
    bool CookieIssued);
