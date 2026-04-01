using Storefront.Api.ProductPages;

namespace Storefront.Api.Checkout;

public sealed class StorefrontCheckoutRequest
{
    public string UserId { get; set; } = string.Empty;

    public string? PaymentMode { get; set; }

    public string? PaymentCallbackUrl { get; set; }
}

internal sealed record StorefrontOrderRequestInfo(
    string Service,
    string RunId,
    string TraceId,
    string RequestId,
    string? CorrelationId);

internal sealed record StorefrontOrderSnapshot(
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
    string CheckoutMode,
    string? BackgroundJobId,
    StorefrontOrderRequestInfo OrderRequest);

internal enum OrderCheckoutClientOutcome
{
    Success,
    DomainFailure,
    Failed
}

internal sealed record OrderCheckoutClientResult(
    OrderCheckoutClientOutcome Outcome,
    StorefrontOrderSnapshot? Order,
    string? ErrorCode,
    string? ErrorDetail,
    int StatusCode,
    StorefrontOrderRequestInfo? OrderRequest,
    bool? ContractSatisfied);

internal sealed record StorefrontCheckoutResponse(
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
    string CheckoutMode,
    string? BackgroundJobId,
    string Source,
    StorefrontOrderRequestInfo Order,
    StorefrontRequestInfo Request);

internal sealed record StorefrontCheckoutFailureResponse(
    string Error,
    string Detail,
    bool ContractSatisfied,
    string UserId,
    string? IdempotencyKey,
    string? PaymentMode,
    string? CheckoutMode,
    string Source,
    StorefrontOrderRequestInfo? Order,
    StorefrontRequestInfo Request);
