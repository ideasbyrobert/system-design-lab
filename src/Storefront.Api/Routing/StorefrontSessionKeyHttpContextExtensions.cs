using Microsoft.AspNetCore.Http;

namespace Storefront.Api.Routing;

internal static class StorefrontSessionKeyHttpContextExtensions
{
    private static readonly object SessionKeyResolutionItemKey = new();

    public static void SetStorefrontSessionKeyResolution(this HttpContext context, StorefrontSessionKeyResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resolution);

        context.Items[SessionKeyResolutionItemKey] = resolution;
    }

    public static StorefrontSessionKeyResolution GetRequiredStorefrontSessionKeyResolution(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Items.TryGetValue(SessionKeyResolutionItemKey, out object? value) &&
            value is StorefrontSessionKeyResolution resolution)
        {
            return resolution;
        }

        throw new InvalidOperationException("Storefront session key resolution is not available for the current request.");
    }
}
