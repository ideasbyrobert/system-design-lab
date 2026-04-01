using System.Text.Json;

namespace Storefront.Api.Checkout;

internal static class OrderCheckoutResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<OrderCheckoutClientResult> ReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.StatusCode is System.Net.HttpStatusCode.OK or System.Net.HttpStatusCode.Accepted)
        {
            OrderCheckoutSuccessPayload payload = await DeserializeRequiredAsync<OrderCheckoutSuccessPayload>(response, cancellationToken);
            StorefrontOrderRequestInfo orderRequest = CreateOrderRequestInfo(payload.Request);

            return new OrderCheckoutClientResult(
                Outcome: OrderCheckoutClientOutcome.Success,
                Order: new StorefrontOrderSnapshot(
                    OrderId: payload.OrderId,
                    Status: payload.Status,
                    ContractSatisfied: payload.ContractSatisfied,
                    PaymentId: payload.PaymentId,
                    PaymentStatus: payload.PaymentStatus,
                    TotalAmountCents: payload.TotalAmountCents,
                    UserId: payload.UserId,
                    CartId: payload.CartId,
                    Region: payload.Region,
                    ItemCount: payload.ItemCount,
                    PaymentMode: payload.PaymentMode,
                    PaymentProviderReference: payload.PaymentProviderReference,
                    PaymentOutcome: payload.PaymentOutcome,
                    PaymentErrorCode: payload.PaymentErrorCode,
                    CheckoutMode: payload.CheckoutMode ?? string.Empty,
                    BackgroundJobId: payload.BackgroundJobId,
                    OrderRequest: orderRequest),
                ErrorCode: null,
                ErrorDetail: null,
                StatusCode: (int)response.StatusCode,
                OrderRequest: orderRequest,
                ContractSatisfied: payload.ContractSatisfied);
        }

        if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
        {
            OrderCheckoutFailurePayload payload = await DeserializeRequiredAsync<OrderCheckoutFailurePayload>(response, cancellationToken);
            StorefrontOrderRequestInfo orderRequest = CreateOrderRequestInfo(payload.Request);

            return new OrderCheckoutClientResult(
                Outcome: OrderCheckoutClientOutcome.DomainFailure,
                Order: null,
                ErrorCode: payload.Error,
                ErrorDetail: payload.Detail,
                StatusCode: (int)response.StatusCode,
                OrderRequest: orderRequest,
                ContractSatisfied: payload.ContractSatisfied);
        }

        return new OrderCheckoutClientResult(
            Outcome: OrderCheckoutClientOutcome.Failed,
            Order: null,
            ErrorCode: $"order_http_{(int)response.StatusCode}",
            ErrorDetail: null,
            StatusCode: (int)response.StatusCode,
            OrderRequest: null,
            ContractSatisfied: null);
    }

    private static async Task<T> DeserializeRequiredAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
        where T : class
    {
        await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        T? payload = await JsonSerializer.DeserializeAsync<T>(responseStream, JsonOptions, cancellationToken);
        return payload ?? throw new JsonException($"Order response body could not be deserialized as {typeof(T).Name}.");
    }

    private static StorefrontOrderRequestInfo CreateOrderRequestInfo(OrderRequestPayload payload) =>
        new(
            Service: "Order.Api",
            RunId: payload.RunId,
            TraceId: payload.TraceId,
            RequestId: payload.RequestId,
            CorrelationId: payload.CorrelationId);

    private sealed record OrderRequestPayload(
        string RunId,
        string TraceId,
        string RequestId,
        string? CorrelationId);

    private sealed record OrderCheckoutSuccessPayload(
        string OrderId,
        string Status,
        bool ContractSatisfied,
        string PaymentId,
        string PaymentStatus,
        int TotalAmountCents,
        string UserId,
        string CartId,
        string Region,
        int ItemCount,
        string PaymentMode,
        string? PaymentProviderReference,
        string PaymentOutcome,
        string? PaymentErrorCode,
        OrderRequestPayload Request)
    {
        public string? CheckoutMode { get; init; }

        public string? BackgroundJobId { get; init; }
    }

    private sealed record OrderCheckoutFailurePayload(
        string Error,
        string Detail,
        bool ContractSatisfied,
        string UserId,
        string? IdempotencyKey,
        string? PaymentMode,
        OrderRequestPayload Request)
    {
        public string? CheckoutMode { get; init; }
    }
}
