using System.Text.Json;

namespace Storefront.Api.CartState;

internal static class CartResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<CartClientResult> ReadAddItemAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.StatusCode == System.Net.HttpStatusCode.OK)
        {
            CartSuccessPayload payload = await DeserializeRequiredAsync<CartSuccessPayload>(response, cancellationToken);

            return new CartClientResult(
                Outcome: CartClientOutcome.Success,
                Cart: new StorefrontCartSnapshot(
                    CartId: payload.CartId,
                    UserId: payload.UserId,
                    Region: payload.Region,
                    Exists: payload.Exists,
                    Status: payload.Status,
                    LoadOutcome: payload.LoadOutcome,
                    MutationOutcome: payload.MutationOutcome,
                    Persisted: payload.Persisted,
                    DistinctItemCount: payload.DistinctItemCount,
                    TotalQuantity: payload.TotalQuantity,
                    TotalPriceCents: payload.TotalPriceCents,
                    Items: payload.Items
                        .Select(item => new StorefrontCartItemSnapshot(
                            item.ProductId,
                            item.Quantity,
                            item.UnitPriceSnapshotCents,
                            item.LineSubtotalCents,
                            item.AddedUtc))
                        .ToArray(),
                    CartRequest: new StorefrontCartServiceRequestInfo(
                        Service: "Cart.Api",
                        RunId: payload.Request.RunId,
                        TraceId: payload.Request.TraceId,
                        RequestId: payload.Request.RequestId,
                        CorrelationId: payload.Request.CorrelationId)),
                ErrorCode: null,
                ErrorDetail: null,
                StatusCode: (int)response.StatusCode,
                CartRequest: new StorefrontCartServiceRequestInfo(
                    Service: "Cart.Api",
                    RunId: payload.Request.RunId,
                    TraceId: payload.Request.TraceId,
                    RequestId: payload.Request.RequestId,
                    CorrelationId: payload.Request.CorrelationId));
        }

        if (response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.NotFound)
        {
            CartErrorPayload payload = await DeserializeRequiredAsync<CartErrorPayload>(response, cancellationToken);
            StorefrontCartServiceRequestInfo request = new(
                Service: "Cart.Api",
                RunId: payload.Request.RunId,
                TraceId: payload.Request.TraceId,
                RequestId: payload.Request.RequestId,
                CorrelationId: payload.Request.CorrelationId);

            return new CartClientResult(
                Outcome: CartClientOutcome.DomainFailure,
                Cart: null,
                ErrorCode: payload.Error,
                ErrorDetail: payload.Detail,
                StatusCode: (int)response.StatusCode,
                CartRequest: request);
        }

        return new CartClientResult(
            Outcome: CartClientOutcome.Failed,
            Cart: null,
            ErrorCode: $"cart_http_{(int)response.StatusCode}",
            ErrorDetail: null,
            StatusCode: (int)response.StatusCode,
            CartRequest: null);
    }

    private static async Task<T> DeserializeRequiredAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
        where T : class
    {
        await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        T? payload = await JsonSerializer.DeserializeAsync<T>(responseStream, JsonOptions, cancellationToken);
        return payload ?? throw new JsonException($"Cart response body could not be deserialized as {typeof(T).Name}.");
    }

    private sealed record CartRequestPayload(
        string RunId,
        string TraceId,
        string RequestId,
        string? CorrelationId);

    private sealed record CartItemPayload(
        string ProductId,
        int Quantity,
        int UnitPriceSnapshotCents,
        int LineSubtotalCents,
        DateTimeOffset AddedUtc);

    private sealed record CartSuccessPayload(
        string? CartId,
        string UserId,
        string Region,
        bool Exists,
        string Status,
        string LoadOutcome,
        string MutationOutcome,
        bool Persisted,
        int DistinctItemCount,
        int TotalQuantity,
        int TotalPriceCents,
        IReadOnlyList<CartItemPayload> Items,
        CartRequestPayload Request);

    private sealed record CartErrorPayload(
        string Error,
        string Detail,
        string? UserId,
        string? ProductId,
        CartRequestPayload Request);
}
