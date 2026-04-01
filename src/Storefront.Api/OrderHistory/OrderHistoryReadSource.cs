namespace Storefront.Api.OrderHistory;

internal enum OrderHistoryReadSource
{
    Local,
    ReadModel,
    PrimaryProjection
}

internal static class OrderHistoryReadSourceParser
{
    public static bool TryParse(string? value, out OrderHistoryReadSource readSource, out string? validationError)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            readSource = OrderHistoryReadSource.Local;
            validationError = null;
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "local":
                readSource = OrderHistoryReadSource.Local;
                validationError = null;
                return true;

            case "read-model":
                readSource = OrderHistoryReadSource.ReadModel;
                validationError = null;
                return true;

            case "primary-projection":
                readSource = OrderHistoryReadSource.PrimaryProjection;
                validationError = null;
                return true;

            default:
                readSource = OrderHistoryReadSource.Local;
                validationError = "Read source must be 'local', 'read-model' or 'primary-projection'.";
                return false;
        }
    }

    public static string ToText(this OrderHistoryReadSource readSource) =>
        readSource switch
        {
            OrderHistoryReadSource.Local => "local",
            OrderHistoryReadSource.ReadModel => "read-model",
            OrderHistoryReadSource.PrimaryProjection => "primary-projection",
            _ => throw new ArgumentOutOfRangeException(nameof(readSource), readSource, "Unknown order-history read source.")
        };
}
