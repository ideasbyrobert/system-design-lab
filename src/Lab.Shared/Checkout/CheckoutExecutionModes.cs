namespace Lab.Shared.Checkout;

public static class CheckoutExecutionModes
{
    public const string Sync = "sync";

    public const string Async = "async";

    public static bool TryParse(string? rawMode, out string normalizedMode)
    {
        if (string.IsNullOrWhiteSpace(rawMode))
        {
            normalizedMode = Sync;
            return true;
        }

        normalizedMode = rawMode.Trim().ToLowerInvariant();
        return normalizedMode is Sync or Async;
    }

    public static bool IsAsync(string mode) =>
        string.Equals(mode, Async, StringComparison.Ordinal);
}
