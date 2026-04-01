using System.Text.Json.Serialization;

namespace Order.Api.Checkout;

public sealed class OrderCheckoutRequest
{
    public string UserId { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;

    public string? PaymentMode { get; set; }

    public string? PaymentCallbackUrl { get; set; }

    [JsonIgnore]
    public bool DebugTelemetryRequested { get; set; }
}

public sealed class OrderPaymentAuthorizationRequest
{
    public string PaymentId { get; set; } = string.Empty;

    public string OrderId { get; set; } = string.Empty;

    public int AmountCents { get; set; }

    public string Currency { get; set; } = "USD";

    public string? PaymentMode { get; set; }

    public string? CallbackUrl { get; set; }

    public bool DebugTelemetryRequested { get; set; }
}

internal sealed record OrderRequestInfo(
    string RunId,
    string TraceId,
    string RequestId,
    string? CorrelationId);

internal sealed record OrderCheckoutResponse(
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
    OrderRequestInfo Request)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CheckoutMode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BackgroundJobId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrderDebugTelemetryInfo? DebugTelemetry { get; init; }
}

internal sealed record OrderCheckoutFailureResponse(
    string Error,
    string Detail,
    bool ContractSatisfied,
    string UserId,
    string? IdempotencyKey,
    string? PaymentMode,
    OrderRequestInfo Request)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CheckoutMode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrderDebugTelemetryInfo? DebugTelemetry { get; init; }
}

internal sealed record OrderHostInfoResponse(
    string ServiceName,
    string Region,
    string RepositoryRoot,
    OrderRequestInfo Request);

internal sealed record OrderDebugStageInfo(
    string StageName,
    double ElapsedMs,
    string Outcome,
    IReadOnlyDictionary<string, string?> Metadata);

internal sealed record OrderDebugDependencyInfo(
    string DependencyName,
    string Route,
    string Region,
    double ElapsedMs,
    int? StatusCode,
    string Outcome,
    IReadOnlyDictionary<string, string?> Metadata,
    IReadOnlyList<string> Notes);

internal sealed record OrderDebugTelemetryInfo(
    IReadOnlyList<OrderDebugStageInfo> StageTimings,
    IReadOnlyList<OrderDebugDependencyInfo> DependencyCalls,
    IReadOnlyList<string> Notes);

internal sealed record PaymentAuthorizationObservation(
    int StatusCode,
    string Mode,
    string Outcome,
    string? ProviderReference,
    string? ErrorCode,
    string? ErrorDetail,
    string? DownstreamRunId,
    string? DownstreamTraceId,
    string? DownstreamRequestId,
    bool CallbackPending,
    int CallbackCountScheduled);

internal sealed record OrderCheckoutExecutionResult(
    int StatusCode,
    bool ContractSatisfied,
    string ResponseOutcome,
    string? ErrorCode,
    OrderCheckoutResponse? Response,
    OrderCheckoutFailureResponse? Failure);
