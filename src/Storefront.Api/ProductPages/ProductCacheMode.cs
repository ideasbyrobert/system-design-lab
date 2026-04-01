namespace Storefront.Api.ProductPages;

internal enum ProductCacheMode
{
    On,
    Off
}

internal static class ProductCacheModeParser
{
    public static bool TryParse(string? value, out ProductCacheMode mode, out string? validationError)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            mode = ProductCacheMode.On;
            validationError = null;
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "on":
                mode = ProductCacheMode.On;
                validationError = null;
                return true;

            case "off":
                mode = ProductCacheMode.Off;
                validationError = null;
                return true;

            default:
                mode = ProductCacheMode.On;
                validationError = "Cache mode must be either 'on' or 'off'.";
                return false;
        }
    }
}
