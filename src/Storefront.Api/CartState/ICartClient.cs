namespace Storefront.Api.CartState;

public interface ICartClient
{
    Task<HttpResponseMessage> AddItemAsync(
        StorefrontCartMutationRequest request,
        string runId,
        string correlationId,
        CancellationToken cancellationToken);
}
