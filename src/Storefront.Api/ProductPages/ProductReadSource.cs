namespace Storefront.Api.ProductPages;

public enum ProductReadSource
{
    Local,
    Primary,
    ReplicaEast,
    ReplicaWest
}

public static class ProductReadSourceParser
{
    public static bool TryParse(string? value, out ProductReadSource readSource, out string? validationError)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            readSource = ProductReadSource.Local;
            validationError = null;
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "local":
                readSource = ProductReadSource.Local;
                validationError = null;
                return true;

            case "primary":
                readSource = ProductReadSource.Primary;
                validationError = null;
                return true;

            case "replica-east":
                readSource = ProductReadSource.ReplicaEast;
                validationError = null;
                return true;

            case "replica-west":
                readSource = ProductReadSource.ReplicaWest;
                validationError = null;
                return true;

            default:
                readSource = ProductReadSource.Local;
                validationError = "Read source must be 'local', 'primary', 'replica-east', or 'replica-west'.";
                return false;
        }
    }

    public static string ToText(this ProductReadSource readSource) =>
        readSource switch
        {
            ProductReadSource.Local => "local",
            ProductReadSource.Primary => "primary",
            ProductReadSource.ReplicaEast => "replica-east",
            ProductReadSource.ReplicaWest => "replica-west",
            _ => throw new ArgumentOutOfRangeException(nameof(readSource), readSource, "Unknown storefront product read source.")
        };
}
