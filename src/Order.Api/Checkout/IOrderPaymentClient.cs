namespace Order.Api.Checkout;

public interface IOrderPaymentClient
{
    Task<HttpResponseMessage> AuthorizeAsync(
        OrderPaymentAuthorizationRequest request,
        string runId,
        string correlationId,
        CancellationToken cancellationToken);
}
