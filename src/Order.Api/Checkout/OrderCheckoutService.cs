using System.Text.Json;
using Lab.Persistence;
using Lab.Persistence.Checkout;
using Lab.Persistence.Entities;
using Lab.Persistence.Queueing;
using Lab.Shared.Checkout;
using Lab.Shared.Configuration;
using Lab.Shared.Contracts;
using Lab.Shared.Networking;
using Lab.Shared.Queueing;
using Lab.Telemetry.RequestTracing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderEntity = Lab.Persistence.Entities.Order;
using PaymentEntity = Lab.Persistence.Entities.Payment;

namespace Order.Api.Checkout;

internal sealed class OrderCheckoutService(
    PrimaryDbContext dbContext,
    CheckoutPersistenceService checkoutPersistenceService,
    IOrderPaymentClient orderPaymentClient,
    IOptions<ServiceEndpointOptions> serviceEndpointOptions,
    IRegionNetworkEnvelopePolicy regionNetworkEnvelopePolicy,
    TimeProvider timeProvider)
{
    private const string ActiveCartStatus = "active";
    private const string OrderStatusPendingPayment = "PendingPayment";
    private const string OrderStatusPaid = "Paid";
    private const string OrderStatusFailed = "Failed";
    private const string OrderStatusCancelled = "Cancelled";
    private const string PaymentStatusPending = "Pending";
    private const string PaymentStatusAuthorized = "Authorized";
    private const string PaymentStatusFailed = "Failed";
    private const string PaymentStatusTimeout = "Timeout";
    private const string PaymentProvider = "PaymentSimulator";
    private const string OrderHistoryProjectionJobType = LabQueueJobTypes.OrderHistoryProjectionUpdate;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> SupportedPaymentModes =
    [
        "fast_success",
        "slow_success",
        "timeout",
        "transient_failure",
        "duplicate_callback",
        "delayed_confirmation"
    ];

    public Task<OrderCheckoutExecutionResult> ExecuteCheckoutAsync(
        OrderCheckoutRequest request,
        string? checkoutMode,
        RequestTraceContext trace,
        CancellationToken cancellationToken)
    {
        if (!CheckoutExecutionModes.TryParse(checkoutMode, out string normalizedMode))
        {
            trace.SetOperationContract(BusinessOperationContracts.OrderCheckout);
            trace.SetUserId(request.UserId);
            trace.RecordInstantStage(
                "request_received",
                metadata: new Dictionary<string, string?>
                {
                    ["userId"] = request.UserId,
                    ["checkoutMode"] = NormalizeOptionalText(checkoutMode),
                    ["idempotencyKey"] = NormalizeOptionalText(request.IdempotencyKey),
                    ["paymentMode"] = NormalizeOptionalText(request.PaymentMode),
                    ["paymentCallbackUrl"] = NormalizeOptionalText(request.PaymentCallbackUrl),
                    ["debugTelemetryRequested"] = request.DebugTelemetryRequested.ToString().ToLowerInvariant()
                });

            return Task.FromResult(CreateFailureResult(
                trace,
                StatusCodes.Status400BadRequest,
                contractSatisfied: false,
                responseOutcome: "validation_failed",
                errorCode: "invalid_checkout_mode",
                detail: $"Checkout mode '{NormalizeOptionalText(checkoutMode) ?? string.Empty}' is not supported.",
                request,
                checkoutMode: null));
        }

        trace.SetOperationContract(
            CheckoutExecutionModes.IsAsync(normalizedMode)
                ? BusinessOperationContracts.CheckoutAsync
                : BusinessOperationContracts.CheckoutSync);

        return CheckoutExecutionModes.IsAsync(normalizedMode)
            ? ExecuteAsyncCheckoutAsync(request, trace, cancellationToken)
            : ExecuteSyncCheckoutAsync(request, trace, cancellationToken);
    }

    public async Task<OrderCheckoutExecutionResult> ExecuteSyncCheckoutAsync(
        OrderCheckoutRequest request,
        RequestTraceContext trace,
        CancellationToken cancellationToken)
    {
        trace.SetUserId(request.UserId);
        trace.RecordInstantStage(
            "request_received",
            metadata: new Dictionary<string, string?>
            {
                ["userId"] = request.UserId,
                ["checkoutMode"] = CheckoutExecutionModes.Sync,
                ["idempotencyKey"] = NormalizeOptionalText(request.IdempotencyKey),
                ["paymentMode"] = NormalizeOptionalText(request.PaymentMode),
                ["paymentCallbackUrl"] = NormalizeOptionalText(request.PaymentCallbackUrl),
                ["debugTelemetryRequested"] = request.DebugTelemetryRequested.ToString().ToLowerInvariant()
            });

        OrderCheckoutExecutionResult? validationFailure = ValidateRequest(request, trace, CheckoutExecutionModes.Sync);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        PaymentEntity? existingPayment;

        using (RequestTraceContext.StageTraceScope stage = trace.BeginStage(
                   "idempotency_checked",
                   new Dictionary<string, string?>
                   {
                       ["idempotencyKey"] = request.IdempotencyKey
                   }))
        {
            existingPayment = await FindExistingPaymentByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken);
            stage.Complete(
                existingPayment is null ? "miss" : "hit",
                new Dictionary<string, string?>
                {
                    ["paymentId"] = existingPayment?.PaymentId,
                    ["orderId"] = existingPayment?.OrderId
                });
        }

        if (existingPayment is not null)
        {
            OrderCheckoutResponse replayResponse = CreateResponseFromStoredState(existingPayment, existingPayment.Order, trace, CheckoutExecutionModes.Sync);
            if (replayResponse.ContractSatisfied)
            {
                trace.MarkContractSatisfied();
            }

            if (!string.IsNullOrWhiteSpace(replayResponse.PaymentErrorCode))
            {
                trace.SetErrorCode(replayResponse.PaymentErrorCode);
            }

            trace.RecordInstantStage(
                "cart_loaded",
                outcome: "reused",
                metadata: new Dictionary<string, string?>
                {
                    ["orderId"] = existingPayment.OrderId,
                    ["paymentId"] = existingPayment.PaymentId
                });
            trace.RecordInstantStage(
                "inventory_reserved",
                outcome: "reused",
                metadata: new Dictionary<string, string?>
                {
                    ["orderId"] = existingPayment.OrderId,
                    ["paymentId"] = existingPayment.PaymentId
                });
            trace.RecordInstantStage(
                "payment_request_started",
                outcome: "reused",
                metadata: new Dictionary<string, string?>
                {
                    ["orderId"] = existingPayment.OrderId,
                    ["paymentId"] = existingPayment.PaymentId,
                    ["paymentMode"] = replayResponse.PaymentMode,
                    ["paymentStatus"] = replayResponse.PaymentStatus
                });
            trace.RecordInstantStage(
                "payment_request_completed",
                outcome: "reused",
                metadata: new Dictionary<string, string?>
                {
                    ["orderId"] = existingPayment.OrderId,
                    ["paymentId"] = existingPayment.PaymentId,
                    ["paymentMode"] = replayResponse.PaymentMode,
                    ["paymentStatus"] = replayResponse.PaymentStatus
                });
            trace.RecordInstantStage(
                "order_persisted",
                outcome: "reused",
                metadata: new Dictionary<string, string?>
                {
                    ["orderId"] = existingPayment.OrderId,
                    ["paymentId"] = existingPayment.PaymentId,
                    ["orderStatus"] = replayResponse.Status,
                    ["paymentStatus"] = replayResponse.PaymentStatus
                });
            trace.AddNote("Order.Api reused the persisted checkout result for the existing idempotency key and skipped inventory reservation plus payment authorization.");

            return new OrderCheckoutExecutionResult(
                StatusCode: StatusCodes.Status200OK,
                ContractSatisfied: replayResponse.ContractSatisfied,
                ResponseOutcome: MapResponseOutcomeFromOrderStatus(replayResponse.Status, CheckoutExecutionModes.Sync),
                ErrorCode: replayResponse.PaymentErrorCode,
                Response: replayResponse,
                Failure: null);
        }

        Cart? cart;

        using (RequestTraceContext.StageTraceScope stage = trace.BeginStage(
                   "cart_loaded",
                   new Dictionary<string, string?>
                   {
                       ["userId"] = request.UserId
                   }))
        {
            cart = await dbContext.Carts
                .Include(item => item.Items)
                .SingleOrDefaultAsync(
                    item => item.UserId == request.UserId && item.Status == ActiveCartStatus,
                    cancellationToken);

            if (cart is null)
            {
                stage.Complete(
                    "missing",
                    new Dictionary<string, string?>
                    {
                        ["exists"] = "false"
                    });
            }
            else
            {
                stage.Complete(
                    cart.Items.Count == 0 ? "empty" : "loaded",
                    new Dictionary<string, string?>
                    {
                        ["exists"] = "true",
                        ["cartId"] = cart.CartId,
                        ["itemCount"] = cart.Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    });
            }
        }

        if (cart is null)
        {
            RecordNoCheckoutStages(trace, "not_attempted", "not_called", "not_created");
            return CreateFailureResult(
                trace,
                StatusCodes.Status404NotFound,
                contractSatisfied: false,
                responseOutcome: "not_found",
                errorCode: "cart_not_found",
                detail: $"No active cart exists for user '{request.UserId}'.",
                request,
                CheckoutExecutionModes.Sync);
        }

        if (cart.Items.Count == 0)
        {
            RecordNoCheckoutStages(trace, "not_attempted", "not_called", "not_created");
            return CreateFailureResult(
                trace,
                StatusCodes.Status409Conflict,
                contractSatisfied: false,
                responseOutcome: "cart_empty",
                errorCode: "cart_empty",
                detail: $"Active cart '{cart.CartId}' has no items to check out.",
                request,
                CheckoutExecutionModes.Sync);
        }

        if (cart.Items.Any(item => item.Quantity <= 0 || item.UnitPriceCents < 0))
        {
            RecordNoCheckoutStages(trace, "not_attempted", "not_called", "not_created");
            return CreateFailureResult(
                trace,
                StatusCodes.Status409Conflict,
                contractSatisfied: false,
                responseOutcome: "invalid_cart_state",
                errorCode: "invalid_cart_state",
                detail: $"Active cart '{cart.CartId}' contains invalid item state.",
                request,
                CheckoutExecutionModes.Sync);
        }

        DateTimeOffset nowUtc = timeProvider.GetUtcNow();
        string orderId = $"order-{Guid.NewGuid():N}";
        string paymentId = $"payment-{Guid.NewGuid():N}";
        string requestedMode = NormalizeOptionalText(request.PaymentMode) ?? "default";

        CheckoutPersistenceResult persistenceResult;

        using (RequestTraceContext.StageTraceScope stage = trace.BeginStage(
                   "inventory_reserved",
                   new Dictionary<string, string?>
                   {
                       ["cartId"] = cart.CartId,
                       ["itemCount"] = cart.Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                       ["paymentId"] = paymentId,
                       ["orderId"] = orderId
                   }))
        {
            persistenceResult = await checkoutPersistenceService.ReserveInventoryAndPersistOrderAsync(
                new CheckoutPersistenceRequest(
                    OrderId: orderId,
                    UserId: cart.UserId,
                    CartId: cart.CartId,
                    Region: cart.Region,
                    OrderStatus: OrderStatusPendingPayment,
                    CreatedUtc: nowUtc,
                    SubmittedUtc: nowUtc,
                    Items: cart.Items
                        .OrderBy(item => item.ProductId, StringComparer.Ordinal)
                        .Select(item => new CheckoutOrderItemPersistenceRequest(
                            item.ProductId,
                            item.Quantity,
                            item.UnitPriceCents))
                        .ToArray(),
                    Payment: new CheckoutPaymentPersistenceRequest(
                        PaymentId: paymentId,
                        Provider: PaymentProvider,
                        IdempotencyKey: request.IdempotencyKey,
                        Mode: requestedMode,
                        Status: PaymentStatusPending,
                        AmountCents: CalculateTotalAmount(cart.Items),
                        ExternalReference: null,
                        ErrorCode: null,
                        AttemptedUtc: nowUtc,
                        ConfirmedUtc: null)),
                cancellationToken);

            stage.Complete(
                persistenceResult.Succeeded ? "reserved" : "failed",
                new Dictionary<string, string?>
                {
                    ["orderId"] = persistenceResult.OrderId ?? orderId,
                    ["paymentId"] = persistenceResult.PaymentId ?? paymentId,
                    ["inventorySnapshotCount"] = persistenceResult.Inventory.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["pendingOrderCreated"] = persistenceResult.Succeeded.ToString().ToLowerInvariant(),
                    ["failureCode"] = persistenceResult.Failure?.Code
                });
        }

        if (!persistenceResult.Succeeded)
        {
            trace.RecordInstantStage(
                "payment_request_started",
                outcome: "not_called",
                metadata: new Dictionary<string, string?>
                {
                    ["paymentMode"] = requestedMode
                });
            trace.RecordInstantStage(
                "payment_request_completed",
                outcome: "not_called",
                metadata: new Dictionary<string, string?>
                {
                    ["paymentMode"] = requestedMode
                });
            trace.RecordInstantStage(
                "order_persisted",
                outcome: "not_created",
                metadata: new Dictionary<string, string?>
                {
                    ["orderId"] = orderId
                });

            return CreateFailureResult(
                trace,
                StatusCodes.Status409Conflict,
                contractSatisfied: false,
                responseOutcome: "inventory_conflict",
                errorCode: persistenceResult.Failure!.Code,
                detail: persistenceResult.Failure.Detail,
                request,
                CheckoutExecutionModes.Sync);
        }

        PaymentAuthorizationObservation paymentObservation;

        trace.RecordInstantStage(
            "payment_request_started",
            metadata: new Dictionary<string, string?>
            {
                ["paymentId"] = paymentId,
                ["orderId"] = orderId,
                ["paymentMode"] = requestedMode
            });

        RegionNetworkEnvelope paymentNetworkEnvelope = regionNetworkEnvelopePolicy.Resolve(serviceEndpointOptions.Value.PaymentSimulatorRegion);

        using (RequestTraceContext.StageTraceScope paymentStage = trace.BeginStage(
                   "payment_request_completed",
                   new Dictionary<string, string?>
                   {
                       ["paymentId"] = paymentId,
                       ["orderId"] = orderId,
                       ["paymentMode"] = requestedMode
                   }))
        {
            using RequestTraceContext.DependencyCallScope dependency = trace.BeginDependencyCall(
                dependencyName: "payment-simulator",
                route: "/payments/authorize",
                region: paymentNetworkEnvelope.TargetRegion,
                metadata: DependencyCallNetworkMetadata.Create(
                    paymentNetworkEnvelope,
                    new Dictionary<string, string?>
                    {
                        ["paymentId"] = paymentId,
                        ["orderId"] = orderId,
                        ["paymentMode"] = requestedMode
                    }),
                notes:
                [
                    $"paymentMode={requestedMode}",
                    $"paymentId={paymentId}",
                    $"orderId={orderId}"
                ]);

            try
            {
                using HttpResponseMessage response = await orderPaymentClient.AuthorizeAsync(
                    new OrderPaymentAuthorizationRequest
                    {
                        PaymentId = paymentId,
                        OrderId = orderId,
                        AmountCents = persistenceResult.TotalPriceCents!.Value,
                        Currency = "USD",
                        PaymentMode = NormalizeOptionalText(request.PaymentMode),
                        CallbackUrl = NormalizeOptionalText(request.PaymentCallbackUrl),
                        DebugTelemetryRequested = request.DebugTelemetryRequested
                    },
                    trace.RunId,
                    trace.CorrelationId ?? trace.RequestId,
                    cancellationToken);

                paymentObservation = await ReadPaymentObservationAsync(response, cancellationToken);

                dependency.Complete(
                    paymentObservation.StatusCode,
                    outcome: GetDependencyOutcome(paymentObservation),
                    metadata: BuildPaymentDependencyMetadata(paymentObservation),
                    notes: BuildPaymentDependencyNotes(paymentObservation));

                paymentStage.Complete(
                    GetPaymentStageOutcome(paymentObservation),
                    new Dictionary<string, string?>
                    {
                        ["paymentStatusCode"] = paymentObservation.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["paymentMode"] = paymentObservation.Mode,
                        ["providerReference"] = paymentObservation.ProviderReference,
                        ["paymentErrorCode"] = paymentObservation.ErrorCode,
                        ["callbackCountScheduled"] = paymentObservation.CallbackCountScheduled.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    });
            }
            catch (Exception exception)
            {
                paymentObservation = new PaymentAuthorizationObservation(
                    StatusCode: StatusCodes.Status502BadGateway,
                    Mode: requestedMode,
                    Outcome: "transport_failure",
                    ProviderReference: null,
                    ErrorCode: "payment_transport_error",
                    ErrorDetail: exception.GetType().Name,
                    DownstreamRunId: null,
                    DownstreamTraceId: null,
                    DownstreamRequestId: null,
                    CallbackPending: false,
                    CallbackCountScheduled: 0);

                dependency.Complete(
                    statusCode: StatusCodes.Status502BadGateway,
                    outcome: "failed",
                    metadata: BuildPaymentDependencyMetadata(paymentObservation),
                    notes:
                    [
                        "payment_transport_error",
                        exception.GetType().Name
                    ]);

                paymentStage.Complete(
                    "transport_failure",
                    new Dictionary<string, string?>
                    {
                        ["paymentMode"] = requestedMode,
                        ["paymentErrorCode"] = "payment_transport_error"
                    });
            }
        }

        CheckoutFinalization finalization = MapFinalization(paymentObservation, nowUtc);

        using (RequestTraceContext.StageTraceScope stage = trace.BeginStage(
                   "order_persisted",
                   new Dictionary<string, string?>
                   {
                       ["orderId"] = orderId,
                       ["paymentId"] = paymentId
                   }))
        {
            await PersistFinalizationAsync(
                orderId,
                paymentId,
                finalization,
                paymentObservation.ProviderReference,
                trace.RunId,
                cancellationToken);

            stage.Complete(
                finalization.StageOutcome,
                new Dictionary<string, string?>
                {
                    ["orderStatus"] = finalization.OrderStatus,
                    ["paymentStatus"] = finalization.PaymentStatus,
                    ["providerReference"] = paymentObservation.ProviderReference,
                    ["paymentErrorCode"] = finalization.PaymentErrorCode
                });
        }

        bool contractSatisfied = DidCheckoutContractSucceed(finalization.OrderStatus, CheckoutExecutionModes.Sync);
        if (contractSatisfied)
        {
            trace.MarkContractSatisfied();
        }

        if (finalization.PaymentErrorCode is not null)
        {
            trace.SetErrorCode(finalization.PaymentErrorCode);
        }

        if (finalization.OrderStatus == OrderStatusFailed)
        {
            trace.AddNote("Synchronous checkout chose the explicit failed-order rule for non-successful payment outcomes.");
        }
        else if (finalization.OrderStatus == OrderStatusPendingPayment)
        {
            trace.AddNote("Synchronous checkout left the order pending because the payment provider reported delayed confirmation.");
            trace.AddNote("The synchronous checkout contract was not met because payment confirmation remained pending at the response boundary.");
        }

        trace.AddNote("Order.Api queued an order-history projection update after persisting the synchronous checkout result.");

        return new OrderCheckoutExecutionResult(
            StatusCode: StatusCodes.Status200OK,
            ContractSatisfied: contractSatisfied,
            ResponseOutcome: finalization.ResponseOutcome,
            ErrorCode: finalization.PaymentErrorCode,
            Response: new OrderCheckoutResponse(
                OrderId: orderId,
                Status: finalization.OrderStatus,
                ContractSatisfied: contractSatisfied,
                PaymentId: paymentId,
                PaymentStatus: finalization.PaymentStatus,
                TotalAmountCents: persistenceResult.TotalPriceCents!.Value,
                UserId: cart.UserId,
                CartId: cart.CartId,
                Region: cart.Region,
                ItemCount: cart.Items.Sum(item => item.Quantity),
                PaymentMode: paymentObservation.Mode,
                PaymentProviderReference: paymentObservation.ProviderReference,
                PaymentOutcome: DerivePaymentOutcome(finalization.PaymentStatus, finalization.PaymentErrorCode, CheckoutExecutionModes.Sync),
                PaymentErrorCode: finalization.PaymentErrorCode,
                Request: CreateRequestInfo(trace))
            {
                CheckoutMode = CheckoutExecutionModes.Sync
            },
            Failure: null);
    }

    private async Task<OrderCheckoutExecutionResult> ExecuteAsyncCheckoutAsync(
        OrderCheckoutRequest request,
        RequestTraceContext trace,
        CancellationToken cancellationToken)
    {
        trace.SetUserId(request.UserId);
        trace.RecordInstantStage(
            "request_received",
            metadata: new Dictionary<string, string?>
            {
                ["userId"] = request.UserId,
                ["checkoutMode"] = CheckoutExecutionModes.Async,
                ["idempotencyKey"] = NormalizeOptionalText(request.IdempotencyKey),
                ["paymentMode"] = NormalizeOptionalText(request.PaymentMode),
                ["paymentCallbackUrl"] = NormalizeOptionalText(request.PaymentCallbackUrl),
                ["debugTelemetryRequested"] = request.DebugTelemetryRequested.ToString().ToLowerInvariant()
            });

        OrderCheckoutExecutionResult? validationFailure = ValidateRequest(request, trace, CheckoutExecutionModes.Async);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        PaymentEntity? existingPayment;

        using (RequestTraceContext.StageTraceScope stage = trace.BeginStage(
                   "idempotency_checked",
                   new Dictionary<string, string?>
                   {
                       ["idempotencyKey"] = request.IdempotencyKey
                   }))
        {
            existingPayment = await FindExistingPaymentByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken);
            stage.Complete(
                existingPayment is null ? "miss" : "hit",
                new Dictionary<string, string?>
                {
                    ["paymentId"] = existingPayment?.PaymentId,
                    ["orderId"] = existingPayment?.OrderId
                });
        }

        if (existingPayment is not null)
        {
            OrderCheckoutResponse replayResponse = CreateResponseFromStoredState(existingPayment, existingPayment.Order, trace, CheckoutExecutionModes.Async);
            if (replayResponse.ContractSatisfied)
            {
                trace.MarkContractSatisfied();
            }

            if (!string.IsNullOrWhiteSpace(replayResponse.PaymentErrorCode))
            {
                trace.SetErrorCode(replayResponse.PaymentErrorCode);
            }

            trace.RecordInstantStage(
                "cart_loaded",
                outcome: "reused",
                metadata: new Dictionary<string, string?>
                {
                    ["orderId"] = existingPayment.OrderId,
                    ["paymentId"] = existingPayment.PaymentId
                });
            trace.RecordInstantStage(
                "inventory_reserved",
                outcome: "reused",
                metadata: new Dictionary<string, string?>
                {
                    ["orderId"] = existingPayment.OrderId,
                    ["paymentId"] = existingPayment.PaymentId
                });
            trace.RecordInstantStage(
                "payment_job_enqueued",
                outcome: "reused",
                metadata: new Dictionary<string, string?>
                {
                    ["orderId"] = existingPayment.OrderId,
                    ["paymentId"] = existingPayment.PaymentId
                });
            trace.RecordInstantStage(
                "order_persisted",
                outcome: "reused",
                metadata: new Dictionary<string, string?>
                {
                    ["orderId"] = existingPayment.OrderId,
                    ["paymentId"] = existingPayment.PaymentId,
                    ["orderStatus"] = replayResponse.Status,
                    ["paymentStatus"] = replayResponse.PaymentStatus
                });
            trace.AddNote("Order.Api reused the persisted checkout result for the existing idempotency key and skipped inventory reservation plus queue enqueue.");

            return new OrderCheckoutExecutionResult(
                StatusCode: replayResponse.Status == OrderStatusPendingPayment ? StatusCodes.Status202Accepted : StatusCodes.Status200OK,
                ContractSatisfied: replayResponse.ContractSatisfied,
                ResponseOutcome: MapResponseOutcomeFromOrderStatus(replayResponse.Status, CheckoutExecutionModes.Async),
                ErrorCode: replayResponse.PaymentErrorCode,
                Response: replayResponse,
                Failure: null);
        }

        Cart? cart;

        using (RequestTraceContext.StageTraceScope stage = trace.BeginStage(
                   "cart_loaded",
                   new Dictionary<string, string?>
                   {
                       ["userId"] = request.UserId
                   }))
        {
            cart = await dbContext.Carts
                .Include(item => item.Items)
                .SingleOrDefaultAsync(
                    item => item.UserId == request.UserId && item.Status == ActiveCartStatus,
                    cancellationToken);

            if (cart is null)
            {
                stage.Complete(
                    "missing",
                    new Dictionary<string, string?>
                    {
                        ["exists"] = "false"
                    });
            }
            else
            {
                stage.Complete(
                    cart.Items.Count == 0 ? "empty" : "loaded",
                    new Dictionary<string, string?>
                    {
                        ["exists"] = "true",
                        ["cartId"] = cart.CartId,
                        ["itemCount"] = cart.Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    });
            }
        }

        if (cart is null)
        {
            RecordNoAsyncCheckoutStages(trace, "not_attempted", "not_enqueued", "not_created");
            return CreateFailureResult(
                trace,
                StatusCodes.Status404NotFound,
                contractSatisfied: false,
                responseOutcome: "not_found",
                errorCode: "cart_not_found",
                detail: $"No active cart exists for user '{request.UserId}'.",
                request,
                CheckoutExecutionModes.Async);
        }

        if (cart.Items.Count == 0)
        {
            RecordNoAsyncCheckoutStages(trace, "not_attempted", "not_enqueued", "not_created");
            return CreateFailureResult(
                trace,
                StatusCodes.Status409Conflict,
                contractSatisfied: false,
                responseOutcome: "cart_empty",
                errorCode: "cart_empty",
                detail: $"Active cart '{cart.CartId}' has no items to check out.",
                request,
                CheckoutExecutionModes.Async);
        }

        if (cart.Items.Any(item => item.Quantity <= 0 || item.UnitPriceCents < 0))
        {
            RecordNoAsyncCheckoutStages(trace, "not_attempted", "not_enqueued", "not_created");
            return CreateFailureResult(
                trace,
                StatusCodes.Status409Conflict,
                contractSatisfied: false,
                responseOutcome: "invalid_cart_state",
                errorCode: "invalid_cart_state",
                detail: $"Active cart '{cart.CartId}' contains invalid item state.",
                request,
                CheckoutExecutionModes.Async);
        }

        DateTimeOffset nowUtc = timeProvider.GetUtcNow();
        string orderId = $"order-{Guid.NewGuid():N}";
        string paymentId = $"payment-{Guid.NewGuid():N}";
        string queueJobId = $"job-{Guid.NewGuid():N}";
        string orderHistoryQueueJobId = $"job-{Guid.NewGuid():N}";
        string requestedMode = NormalizeOptionalText(request.PaymentMode) ?? "default";

        string paymentQueuePayloadJson = JsonSerializer.Serialize(
            new PaymentConfirmationRetryJobPayload(paymentId, orderId, trace.RunId),
            JsonOptions);
        string orderHistoryQueuePayloadJson = JsonSerializer.Serialize(
            new OrderHistoryProjectionUpdateJobPayload(orderId, cart.UserId, trace.RunId),
            JsonOptions);

        CheckoutPersistenceResult persistenceResult;

        using (RequestTraceContext.StageTraceScope stage = trace.BeginStage(
                   "inventory_reserved",
                   new Dictionary<string, string?>
                   {
                       ["cartId"] = cart.CartId,
                       ["itemCount"] = cart.Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                       ["paymentId"] = paymentId,
                       ["orderId"] = orderId
                   }))
        {
            persistenceResult = await checkoutPersistenceService.ReserveInventoryPersistOrderAndEnqueueJobsAsync(
                new CheckoutPersistenceRequest(
                    OrderId: orderId,
                    UserId: cart.UserId,
                    CartId: cart.CartId,
                    Region: cart.Region,
                    OrderStatus: OrderStatusPendingPayment,
                    CreatedUtc: nowUtc,
                    SubmittedUtc: nowUtc,
                    Items: cart.Items
                        .OrderBy(item => item.ProductId, StringComparer.Ordinal)
                        .Select(item => new CheckoutOrderItemPersistenceRequest(
                            item.ProductId,
                            item.Quantity,
                            item.UnitPriceCents))
                        .ToArray(),
                    Payment: new CheckoutPaymentPersistenceRequest(
                        PaymentId: paymentId,
                        Provider: PaymentProvider,
                        IdempotencyKey: request.IdempotencyKey,
                        Mode: requestedMode,
                        Status: PaymentStatusPending,
                        AmountCents: CalculateTotalAmount(cart.Items),
                        ExternalReference: null,
                        ErrorCode: null,
                        AttemptedUtc: nowUtc,
                        ConfirmedUtc: null)),
                [
                    new EnqueueQueueJobRequest(
                        QueueJobId: queueJobId,
                        JobType: LabQueueJobTypes.PaymentConfirmationRetry,
                        PayloadJson: paymentQueuePayloadJson,
                        EnqueuedUtc: nowUtc),
                    new EnqueueQueueJobRequest(
                        QueueJobId: orderHistoryQueueJobId,
                        JobType: OrderHistoryProjectionJobType,
                        PayloadJson: orderHistoryQueuePayloadJson,
                        EnqueuedUtc: nowUtc)
                ],
                cancellationToken);

            stage.Complete(
                persistenceResult.Succeeded ? "reserved" : "failed",
                new Dictionary<string, string?>
                {
                    ["orderId"] = persistenceResult.OrderId ?? orderId,
                    ["paymentId"] = persistenceResult.PaymentId ?? paymentId,
                    ["inventorySnapshotCount"] = persistenceResult.Inventory.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["pendingOrderCreated"] = persistenceResult.Succeeded.ToString().ToLowerInvariant(),
                    ["failureCode"] = persistenceResult.Failure?.Code
                });
        }

        if (!persistenceResult.Succeeded)
        {
            RecordNoAsyncCheckoutStages(trace, "failed", "not_enqueued", "not_created");
            return CreateFailureResult(
                trace,
                StatusCodes.Status409Conflict,
                contractSatisfied: false,
                responseOutcome: "inventory_conflict",
                errorCode: persistenceResult.Failure!.Code,
                detail: persistenceResult.Failure.Detail,
                request,
                CheckoutExecutionModes.Async);
        }

        trace.RecordInstantStage(
            "payment_job_enqueued",
            outcome: "queued",
            metadata: new Dictionary<string, string?>
            {
                ["queueJobId"] = persistenceResult.QueueJobId,
                ["orderId"] = orderId,
                ["paymentId"] = paymentId,
                ["orderHistoryQueueJobId"] = orderHistoryQueueJobId
            });
        trace.RecordInstantStage(
            "order_persisted",
            outcome: "pending_payment",
            metadata: new Dictionary<string, string?>
            {
                ["orderId"] = orderId,
                ["paymentId"] = paymentId,
                ["orderStatus"] = OrderStatusPendingPayment,
                ["paymentStatus"] = PaymentStatusPending,
                ["queueJobId"] = persistenceResult.QueueJobId,
                ["orderHistoryQueueJobId"] = orderHistoryQueueJobId
            });

        trace.MarkContractSatisfied();
        trace.AddNote("Asynchronous checkout accepted the order and delegated payment confirmation to Worker.");
        trace.AddNote("The response boundary closed before payment confirmation, so the background queue now owns the remaining work.");
        trace.AddNote("Order.Api also queued an order-history projection update, so Storefront reads may lag until Worker applies it.");

        return new OrderCheckoutExecutionResult(
            StatusCode: StatusCodes.Status202Accepted,
            ContractSatisfied: true,
            ResponseOutcome: "accepted_pending",
            ErrorCode: null,
            Response: new OrderCheckoutResponse(
                OrderId: orderId,
                Status: OrderStatusPendingPayment,
                ContractSatisfied: true,
                PaymentId: paymentId,
                PaymentStatus: PaymentStatusPending,
                TotalAmountCents: persistenceResult.TotalPriceCents!.Value,
                UserId: cart.UserId,
                CartId: cart.CartId,
                Region: cart.Region,
                ItemCount: cart.Items.Sum(item => item.Quantity),
                PaymentMode: requestedMode,
                PaymentProviderReference: null,
                PaymentOutcome: "queued_for_background_confirmation",
                PaymentErrorCode: null,
                Request: CreateRequestInfo(trace))
            {
                CheckoutMode = CheckoutExecutionModes.Async,
                BackgroundJobId = persistenceResult.QueueJobId
            },
            Failure: null);
    }

    private static OrderCheckoutExecutionResult? ValidateRequest(
        OrderCheckoutRequest request,
        RequestTraceContext trace,
        string checkoutMode)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return CreateFailureResult(
                trace,
                StatusCodes.Status400BadRequest,
                contractSatisfied: false,
                responseOutcome: "validation_failed",
                errorCode: "invalid_user_id",
                detail: "A non-empty user identifier is required.",
                request,
                checkoutMode);
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return CreateFailureResult(
                trace,
                StatusCodes.Status400BadRequest,
                contractSatisfied: false,
                responseOutcome: "validation_failed",
                errorCode: "missing_idempotency_key",
                detail: $"The '{Lab.Shared.Http.LabHeaderNames.IdempotencyKey}' header is required for checkout.",
                request,
                checkoutMode);
        }

        string? paymentMode = NormalizeOptionalText(request.PaymentMode);
        if (paymentMode is not null && !SupportedPaymentModes.Contains(paymentMode))
        {
            return CreateFailureResult(
                trace,
                StatusCodes.Status400BadRequest,
                contractSatisfied: false,
                responseOutcome: "validation_failed",
                errorCode: "invalid_payment_mode",
                detail: $"Payment mode '{paymentMode}' is not supported by {checkoutMode} checkout.",
                request,
                checkoutMode);
        }

        return null;
    }

    private static void RecordNoCheckoutStages(
        RequestTraceContext trace,
        string persistenceOutcome,
        string paymentOutcome,
        string finalizationOutcome)
    {
        trace.RecordInstantStage("inventory_reserved", outcome: persistenceOutcome);
        trace.RecordInstantStage("payment_request_started", outcome: paymentOutcome);
        trace.RecordInstantStage("payment_request_completed", outcome: paymentOutcome);
        trace.RecordInstantStage("order_persisted", outcome: finalizationOutcome);
    }

    private static void RecordNoAsyncCheckoutStages(
        RequestTraceContext trace,
        string persistenceOutcome,
        string queueOutcome,
        string finalizationOutcome)
    {
        trace.RecordInstantStage("inventory_reserved", outcome: persistenceOutcome);
        trace.RecordInstantStage("payment_job_enqueued", outcome: queueOutcome);
        trace.RecordInstantStage("order_persisted", outcome: finalizationOutcome);
    }

    private async Task PersistFinalizationAsync(
        string orderId,
        string paymentId,
        CheckoutFinalization finalization,
        string? providerReference,
        string runId,
        CancellationToken cancellationToken)
    {
        OrderEntity order = await dbContext.Orders
            .Include(item => item.Payments)
            .SingleAsync(item => item.OrderId == orderId, cancellationToken);

        PaymentEntity payment = order.Payments.Single(item => item.PaymentId == paymentId);
        order.Status = finalization.OrderStatus;
        payment.Status = finalization.PaymentStatus;
        payment.ExternalReference = NormalizeOptionalText(providerReference);
        payment.ErrorCode = NormalizeOptionalText(finalization.PaymentErrorCode);
        payment.ConfirmedUtc = finalization.ConfirmedUtc;
        dbContext.QueueJobs.Add(CreateOrderHistoryProjectionQueueJob(order, runId, timeProvider.GetUtcNow()));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static CheckoutFinalization MapFinalization(PaymentAuthorizationObservation observation, DateTimeOffset nowUtc)
    {
        if (observation.StatusCode == StatusCodes.Status200OK &&
            string.Equals(observation.Outcome, "authorized", StringComparison.Ordinal))
        {
            return new CheckoutFinalization(
                OrderStatus: OrderStatusPaid,
                PaymentStatus: PaymentStatusAuthorized,
                ResponseOutcome: "paid",
                StageOutcome: "paid",
                PaymentErrorCode: null,
                ConfirmedUtc: nowUtc);
        }

        if (observation.StatusCode == StatusCodes.Status202Accepted ||
            string.Equals(observation.Outcome, "pending_confirmation", StringComparison.Ordinal))
        {
            return new CheckoutFinalization(
                OrderStatus: OrderStatusPendingPayment,
                PaymentStatus: PaymentStatusPending,
                ResponseOutcome: "pending_payment",
                StageOutcome: "pending_payment",
                PaymentErrorCode: observation.ErrorCode,
                ConfirmedUtc: null);
        }

        string paymentErrorCode = observation.ErrorCode ?? "payment_failed";
        string paymentStatus = observation.StatusCode == StatusCodes.Status504GatewayTimeout ||
                               string.Equals(paymentErrorCode, "simulated_timeout", StringComparison.Ordinal)
            ? PaymentStatusTimeout
            : PaymentStatusFailed;

        return new CheckoutFinalization(
            OrderStatus: OrderStatusFailed,
            PaymentStatus: paymentStatus,
            ResponseOutcome: "failed",
            StageOutcome: "failed",
            PaymentErrorCode: paymentErrorCode,
            ConfirmedUtc: null);
    }

    private static async Task<PaymentAuthorizationObservation> ReadPaymentObservationAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string payload = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;

            return new PaymentAuthorizationObservation(
                StatusCode: (int)response.StatusCode,
                Mode: ReadString(root, "mode") ?? "unknown",
                Outcome: NormalizePaymentOutcome((int)response.StatusCode, ReadString(root, "outcome"), ReadString(root, "error")),
                ProviderReference: ReadString(root, "providerReference"),
                ErrorCode: ReadString(root, "error"),
                ErrorDetail: ReadString(root, "detail"),
                DownstreamRunId: ReadHeader(response, Lab.Shared.Http.LabHeaderNames.RunId),
                DownstreamTraceId: ReadHeader(response, Lab.Shared.Http.LabHeaderNames.TraceId),
                DownstreamRequestId: ReadHeader(response, Lab.Shared.Http.LabHeaderNames.RequestId),
                CallbackPending: ReadBoolean(root, "callbackPending"),
                CallbackCountScheduled: ReadInt32(root, "callbackCountScheduled"));
        }
        catch (JsonException)
        {
            return new PaymentAuthorizationObservation(
                StatusCode: (int)response.StatusCode,
                Mode: "unknown",
                Outcome: "invalid_response",
                ProviderReference: null,
                ErrorCode: "invalid_payment_response",
                ErrorDetail: "Payment simulator returned malformed JSON.",
                DownstreamRunId: ReadHeader(response, Lab.Shared.Http.LabHeaderNames.RunId),
                DownstreamTraceId: ReadHeader(response, Lab.Shared.Http.LabHeaderNames.TraceId),
                DownstreamRequestId: ReadHeader(response, Lab.Shared.Http.LabHeaderNames.RequestId),
                CallbackPending: false,
                CallbackCountScheduled: 0);
        }
    }

    private static string GetDependencyOutcome(PaymentAuthorizationObservation observation) =>
        observation.StatusCode switch
        {
            StatusCodes.Status200OK => "success",
            StatusCodes.Status202Accepted => "pending_confirmation",
            _ => "failed"
        };

    private static string GetPaymentStageOutcome(PaymentAuthorizationObservation observation) =>
        observation.StatusCode switch
        {
            StatusCodes.Status200OK => "authorized",
            StatusCodes.Status202Accepted => "pending_confirmation",
            StatusCodes.Status504GatewayTimeout => "timeout",
            _ => observation.ErrorCode ?? "failed"
        };

    private static IEnumerable<string> BuildPaymentDependencyNotes(PaymentAuthorizationObservation observation)
    {
        List<string> notes = [];

        if (!string.IsNullOrWhiteSpace(observation.ProviderReference))
        {
            notes.Add($"providerReference={observation.ProviderReference}");
        }

        notes.Add($"paymentMode={observation.Mode}");
        notes.Add($"paymentOutcome={observation.Outcome}");

        if (!string.IsNullOrWhiteSpace(observation.ErrorCode))
        {
            notes.Add($"paymentError={observation.ErrorCode}");
        }

        return notes;
    }

    private static IReadOnlyDictionary<string, string?> BuildPaymentDependencyMetadata(PaymentAuthorizationObservation observation) =>
        new Dictionary<string, string?>
        {
            ["paymentMode"] = observation.Mode,
            ["paymentOutcome"] = observation.Outcome,
            ["providerReference"] = observation.ProviderReference,
            ["paymentErrorCode"] = observation.ErrorCode,
            ["callbackPending"] = observation.CallbackPending.ToString().ToLowerInvariant(),
            ["callbackCountScheduled"] = observation.CallbackCountScheduled.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["downstreamRunId"] = observation.DownstreamRunId,
            ["downstreamTraceId"] = observation.DownstreamTraceId,
            ["downstreamRequestId"] = observation.DownstreamRequestId
        };

    private static OrderCheckoutExecutionResult CreateFailureResult(
        RequestTraceContext trace,
        int statusCode,
        bool contractSatisfied,
        string responseOutcome,
        string errorCode,
        string detail,
        OrderCheckoutRequest request,
        string? checkoutMode) =>
        new(
            StatusCode: statusCode,
            ContractSatisfied: contractSatisfied,
            ResponseOutcome: responseOutcome,
            ErrorCode: errorCode,
            Response: null,
            Failure: new OrderCheckoutFailureResponse(
                Error: errorCode,
                Detail: detail,
                ContractSatisfied: contractSatisfied,
                UserId: request.UserId,
                IdempotencyKey: NormalizeOptionalText(request.IdempotencyKey),
                PaymentMode: NormalizeOptionalText(request.PaymentMode),
                Request: CreateRequestInfo(trace))
            {
                CheckoutMode = checkoutMode
            });

    private Task<PaymentEntity?> FindExistingPaymentByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        dbContext.Payments
            .AsNoTracking()
            .Include(item => item.Order)
            .ThenInclude(order => order.Items)
            .SingleOrDefaultAsync(item => item.IdempotencyKey == idempotencyKey, cancellationToken);

    private static OrderCheckoutResponse CreateResponseFromStoredState(
        PaymentEntity payment,
        OrderEntity order,
        RequestTraceContext trace,
        string checkoutMode)
    {
        string paymentMode = NormalizeOptionalText(payment.Mode) ?? "unknown";
        string? paymentErrorCode = NormalizeOptionalText(payment.ErrorCode);

        return new OrderCheckoutResponse(
            OrderId: order.OrderId,
            Status: order.Status,
            ContractSatisfied: DidCheckoutContractSucceed(order.Status, checkoutMode),
            PaymentId: payment.PaymentId,
            PaymentStatus: payment.Status,
            TotalAmountCents: order.TotalPriceCents,
            UserId: order.UserId,
            CartId: order.CartId ?? string.Empty,
            Region: order.Region,
            ItemCount: order.Items.Sum(item => item.Quantity),
            PaymentMode: paymentMode,
            PaymentProviderReference: NormalizeOptionalText(payment.ExternalReference),
            PaymentOutcome: DerivePaymentOutcome(payment.Status, paymentErrorCode, checkoutMode),
            PaymentErrorCode: paymentErrorCode,
            Request: CreateRequestInfo(trace))
        {
            CheckoutMode = checkoutMode
        };
    }

    private static string DerivePaymentOutcome(
        string paymentStatus,
        string? paymentErrorCode,
        string checkoutMode) =>
        paymentStatus switch
        {
            PaymentStatusAuthorized => "authorized",
            PaymentStatusPending when CheckoutExecutionModes.IsAsync(checkoutMode) => "queued_for_background_confirmation",
            PaymentStatusPending => "pending_confirmation",
            PaymentStatusTimeout => "timeout",
            PaymentStatusFailed when string.Equals(paymentErrorCode, "simulated_timeout", StringComparison.Ordinal) => "timeout",
            _ => "failed"
        };

    private static string MapResponseOutcomeFromOrderStatus(string orderStatus, string checkoutMode) =>
        orderStatus switch
        {
            OrderStatusPaid => "paid",
            OrderStatusPendingPayment when CheckoutExecutionModes.IsAsync(checkoutMode) => "accepted_pending",
            OrderStatusPendingPayment => "pending_payment",
            OrderStatusFailed => "failed",
            OrderStatusCancelled => "cancelled",
            _ => "replayed"
        };

    private static bool DidCheckoutContractSucceed(string orderStatus, string checkoutMode) =>
        CheckoutExecutionModes.IsAsync(checkoutMode)
            ? !string.Equals(orderStatus, OrderStatusFailed, StringComparison.Ordinal) &&
              !string.Equals(orderStatus, OrderStatusCancelled, StringComparison.Ordinal)
            : !string.Equals(orderStatus, OrderStatusPendingPayment, StringComparison.Ordinal);

    private static OrderRequestInfo CreateRequestInfo(RequestTraceContext trace) =>
        new(
            RunId: trace.RunId,
            TraceId: trace.TraceId,
            RequestId: trace.RequestId,
            CorrelationId: trace.CorrelationId);

    private static int CalculateTotalAmount(IEnumerable<CartItem> items)
    {
        long total = 0;

        foreach (CartItem item in items)
        {
            total += (long)item.Quantity * item.UnitPriceCents;
        }

        return checked((int)total);
    }

    private static QueueJob CreateOrderHistoryProjectionQueueJob(
        OrderEntity order,
        string runId,
        DateTimeOffset enqueuedUtc) =>
        new()
        {
            QueueJobId = $"job-{Guid.NewGuid():N}",
            JobType = OrderHistoryProjectionJobType,
            PayloadJson = JsonSerializer.Serialize(
                new OrderHistoryProjectionUpdateJobPayload(order.OrderId, order.UserId, runId),
                JsonOptions),
            Status = QueueJobStatuses.Pending,
            AvailableUtc = enqueuedUtc,
            EnqueuedUtc = enqueuedUtc,
            RetryCount = 0
        };

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBoolean(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out JsonElement value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        value.GetBoolean();

    private static int ReadInt32(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    private static string? ReadHeader(HttpResponseMessage response, string headerName) =>
        response.Headers.TryGetValues(headerName, out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;

    private static string NormalizePaymentOutcome(int statusCode, string? outcome, string? errorCode)
    {
        if (!string.IsNullOrWhiteSpace(outcome))
        {
            return outcome.Trim();
        }

        return statusCode switch
        {
            StatusCodes.Status200OK => "authorized",
            StatusCodes.Status202Accepted => "pending_confirmation",
            StatusCodes.Status504GatewayTimeout => "timeout",
            _ when string.Equals(errorCode, "simulated_timeout", StringComparison.Ordinal) => "timeout",
            _ => "failed"
        };
    }

    private sealed record CheckoutFinalization(
        string OrderStatus,
        string PaymentStatus,
        string ResponseOutcome,
        string StageOutcome,
        string? PaymentErrorCode,
        DateTimeOffset? ConfirmedUtc);
}
