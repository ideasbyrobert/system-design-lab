namespace Storefront.Api.Checkout;

public interface IOrderCheckoutClient
{
    Task<HttpResponseMessage> CheckoutAsync(
        StorefrontCheckoutRequest request,
        string checkoutMode,
        string idempotencyKey,
        string runId,
        string correlationId,
        bool debugTelemetryRequested,
        CancellationToken cancellationToken);
}
