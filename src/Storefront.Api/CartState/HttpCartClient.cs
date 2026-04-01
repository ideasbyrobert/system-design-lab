using System.Net.Http.Json;
using Lab.Shared.Http;

namespace Storefront.Api.CartState;

internal sealed class HttpCartClient(HttpClient httpClient) : ICartClient
{
    public async Task<HttpResponseMessage> AddItemAsync(
        StorefrontCartMutationRequest request,
        string runId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        using HttpRequestMessage message = new(HttpMethod.Post, "/cart/items")
        {
            Content = JsonContent.Create(request)
        };

        message.Headers.TryAddWithoutValidation(LabHeaderNames.RunId, runId);
        message.Headers.TryAddWithoutValidation(LabHeaderNames.CorrelationId, correlationId);

        return await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
