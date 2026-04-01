using System.Text.Json;
using Lab.Persistence.DependencyInjection;
using Lab.Shared.Caching;
using Lab.Shared.Checkout;
using Lab.Shared.Contracts;
using Lab.Shared.Configuration;
using Lab.Shared.Http;
using Lab.Shared.Logging;
using Lab.Shared.Networking;
using Lab.Shared.RegionalReads;
using Lab.Telemetry.AspNetCore;
using Lab.Telemetry.DependencyInjection;
using Lab.Telemetry.Persistence;
using Lab.Telemetry.RequestTracing;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Storefront.Api.CartState;
using Storefront.Api.Checkout;
using Storefront.Api.LabEndpoints;
using Storefront.Api.OrderHistory;
using Storefront.Api.ProductPages;
using Storefront.Api.RateLimiting;
using Storefront.Api.Routing;
using Lab.Shared.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLabConfiguration(builder.Configuration, builder.Environment);
builder.Services.AddLabTelemetry();
builder.Services.AddRequestTraceJsonlWriter();
builder.Services.AddLabTokenBucketRateLimiting();
builder.Services.AddInMemoryLabCache();
builder.Services.AddReadModelPersistence();
builder.Services.AddScoped<StorefrontProductDetailCache>();
builder.Services.AddScoped<StorefrontOrderHistoryReadService>();
builder.Services.AddHttpClient<ICatalogProductClient, HttpCatalogProductClient>((serviceProvider, client) =>
{
    ServiceEndpointOptions options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceEndpointOptions>>().Value;
    client.BaseAddress = new Uri(EnsureTrailingSlash(options.CatalogBaseUrl), UriKind.Absolute);
})
    .AddRegionLatencyInjection(
        "catalog-api",
        serviceProvider => serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceEndpointOptions>>().Value.CatalogRegion);
builder.Services.AddHttpClient<ICartClient, HttpCartClient>((serviceProvider, client) =>
{
    ServiceEndpointOptions options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceEndpointOptions>>().Value;
    client.BaseAddress = new Uri(EnsureTrailingSlash(options.CartBaseUrl), UriKind.Absolute);
})
    .AddRegionLatencyInjection(
        "cart-api",
        serviceProvider => serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceEndpointOptions>>().Value.CartRegion);
builder.Services.AddHttpClient<IOrderCheckoutClient, HttpOrderCheckoutClient>((serviceProvider, client) =>
{
    ServiceEndpointOptions options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceEndpointOptions>>().Value;
    client.BaseAddress = new Uri(EnsureTrailingSlash(options.OrderBaseUrl), UriKind.Absolute);
})
    .AddRegionLatencyInjection(
        "order-api",
        serviceProvider => serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceEndpointOptions>>().Value.OrderRegion);
builder.Logging.AddLabOperationalFileLogging();
builder.Services.AddProblemDetails();

var app = builder.Build();
app.LogResolvedLabEnvironment();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        ILogger logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Storefront.Exceptions");
        IProblemDetailsService problemDetails = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        IExceptionHandlerFeature? exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();

        logger.LogError(exceptionFeature?.Error, "Unhandled exception reached the Storefront exception boundary.");

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Unhandled Storefront error",
                Detail = "The Storefront host hit an unhandled exception while processing the request.",
                Extensions =
                {
                    ["requestId"] = context.TraceIdentifier
                }
            }
        });
    });
});

app.UseRequestTracing();
app.UseStorefrontSessionKeyConvention();

app.MapGet("/", (
    EnvironmentLayout layout,
    IRequestTraceContextAccessor traceAccessor) =>
{
    RequestTraceContext trace = GetRequiredTraceContext(traceAccessor);

    using (trace.BeginStage("describe_storefront_host", new Dictionary<string, string?>
    {
        ["service"] = layout.ServiceName,
        ["region"] = layout.CurrentRegion
    }))
    {
    }

    trace.MarkContractSatisfied();
    trace.AddNote("Storefront host-info endpoint completed successfully.");

    return Results.Ok(new
    {
        layout.ServiceName,
        layout.CurrentRegion,
        layout.RepositoryRoot,
        Request = new
        {
            trace.RunId,
            trace.TraceId,
            trace.RequestId,
            trace.CorrelationId
        }
    });
})
    .WithOperationContract(BusinessOperationContracts.StorefrontHostInfo);

app.MapGet("/health", (
    EnvironmentLayout layout,
    IRequestTraceContextAccessor traceAccessor) =>
{
    RequestTraceContext trace = GetRequiredTraceContext(traceAccessor);

    using (trace.BeginStage("health_check", new Dictionary<string, string?>
    {
        ["service"] = layout.ServiceName,
        ["region"] = layout.CurrentRegion
    }))
    {
    }

    trace.MarkContractSatisfied();
    trace.AddNote("Health endpoint completed successfully.");

    return Results.Ok(new
    {
        status = "ok",
        layout.ServiceName,
        layout.CurrentRegion,
        Request = new
        {
            trace.RunId,
            trace.TraceId,
            trace.RequestId,
            trace.CorrelationId
        }
    });
})
    .WithOperationContract(BusinessOperationContracts.HealthCheck);

app.MapPost("/cart/items", async (
    [FromBody] StorefrontCartMutationRequest request,
    HttpContext httpContext,
    EnvironmentLayout layout,
    Microsoft.Extensions.Options.IOptions<ServiceEndpointOptions> serviceEndpointOptions,
    IRegionNetworkEnvelopePolicy regionNetworkEnvelopePolicy,
    ICartClient cartClient,
    IRequestTraceContextAccessor traceAccessor,
    CancellationToken cancellationToken) =>
{
    RequestTraceContext trace = GetRequiredTraceContext(traceAccessor);
    StorefrontSessionKeyResolution session = httpContext.GetRequiredStorefrontSessionKeyResolution();
    RegionNetworkEnvelope cartNetworkEnvelope = regionNetworkEnvelopePolicy.Resolve(serviceEndpointOptions.Value.CartRegion);
    trace.SetUserId(request.UserId);
    trace.RecordInstantStage(
        "request_received",
        metadata: new Dictionary<string, string?>
        {
            ["userId"] = request.UserId,
            ["productId"] = request.ProductId,
            ["quantity"] = request.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["sessionKey"] = session.SessionKey,
            ["sessionKeySource"] = session.Source,
            ["sessionCookieIssued"] = session.CookieIssued.ToString().ToLowerInvariant(),
            ["debugTelemetryRequested"] = IsDebugTelemetryRequested(httpContext.Request).ToString().ToLowerInvariant()
        });

    if (string.IsNullOrWhiteSpace(request.UserId))
    {
        trace.MarkContractSatisfied();
        trace.SetErrorCode("invalid_user_id");
        trace.RecordInstantStage(
            "response_sent",
            outcome: "validation_failed",
            metadata: new Dictionary<string, string?>
            {
                ["statusCode"] = StatusCodes.Status400BadRequest.ToString(),
                ["error"] = "invalid_user_id",
                ["userId"] = request.UserId,
                ["productId"] = request.ProductId,
                ["sessionKey"] = session.SessionKey
            });

        return Results.BadRequest(CreateCartFailureResponse(
            trace,
            request.UserId,
            request.ProductId,
            null,
            "invalid_user_id",
            "A non-empty user identifier is required."));
    }

    if (string.IsNullOrWhiteSpace(request.ProductId))
    {
        trace.MarkContractSatisfied();
        trace.SetErrorCode("invalid_product_id");
        trace.RecordInstantStage(
            "response_sent",
            outcome: "validation_failed",
            metadata: new Dictionary<string, string?>
            {
                ["statusCode"] = StatusCodes.Status400BadRequest.ToString(),
                ["error"] = "invalid_product_id",
                ["userId"] = request.UserId,
                ["productId"] = request.ProductId,
                ["sessionKey"] = session.SessionKey
            });

        return Results.BadRequest(CreateCartFailureResponse(
            trace,
            request.UserId,
            request.ProductId,
            null,
            "invalid_product_id",
            "A non-empty product identifier is required."));
    }

    if (request.Quantity <= 0)
    {
        trace.MarkContractSatisfied();
        trace.SetErrorCode("invalid_quantity");
        trace.RecordInstantStage(
            "response_sent",
            outcome: "validation_failed",
            metadata: new Dictionary<string, string?>
            {
                ["statusCode"] = StatusCodes.Status400BadRequest.ToString(),
                ["error"] = "invalid_quantity",
                ["userId"] = request.UserId,
                ["productId"] = request.ProductId,
                ["sessionKey"] = session.SessionKey
            });

        return Results.BadRequest(CreateCartFailureResponse(
            trace,
            request.UserId,
            request.ProductId,
            null,
            "invalid_quantity",
            "Quantity must be greater than zero for add-to-cart."));
    }

    trace.RecordInstantStage(
        "cart_call_started",
        metadata: new Dictionary<string, string?>
        {
            ["userId"] = request.UserId,
            ["productId"] = request.ProductId,
            ["route"] = "/cart/items",
            ["sessionKey"] = session.SessionKey
        });

    CartClientResult cartResult;

    using (RequestTraceContext.DependencyCallScope dependency = trace.BeginDependencyCall(
        dependencyName: "cart-api",
        route: "/cart/items",
        region: cartNetworkEnvelope.TargetRegion,
        metadata: DependencyCallNetworkMetadata.Create(cartNetworkEnvelope),
        notes:
        [
            "operation:add-item-to-cart"
        ]))
    {
        try
        {
            using HttpResponseMessage response = await cartClient.AddItemAsync(
                request,
                trace.RunId,
                trace.CorrelationId ?? trace.RequestId,
                cancellationToken);

            cartResult = await CartResponseParser.ReadAddItemAsync(response, cancellationToken);

            dependency.Complete(
                statusCode: cartResult.StatusCode,
                outcome: ToCartDependencyOutcome(cartResult.Outcome),
                notes: CreateDependencyNotes(cartResult.ErrorCode));
        }
        catch (HttpRequestException exception)
        {
            dependency.Complete(
                outcome: "transport_error",
                notes:
                [
                    $"exception:{exception.GetType().Name}"
                ]);

            trace.SetErrorCode("cart_transport_error");
            trace.AddNote("Storefront could not reach Cart.Api.");
            trace.RecordInstantStage(
                "cart_call_completed",
                outcome: "transport_error",
                metadata: new Dictionary<string, string?>
                {
                    ["userId"] = request.UserId,
                    ["productId"] = request.ProductId,
                    ["error"] = "cart_transport_error"
                });
            trace.RecordInstantStage(
                "response_sent",
                outcome: "upstream_failure",
                metadata: new Dictionary<string, string?>
                {
                    ["statusCode"] = StatusCodes.Status502BadGateway.ToString(),
                    ["error"] = "cart_transport_error",
                    ["userId"] = request.UserId,
                    ["productId"] = request.ProductId,
                    ["sessionKey"] = session.SessionKey
                });

            return Results.Json(
                CreateCartFailureResponse(
                    trace,
                    request.UserId,
                    request.ProductId,
                    null,
                    "cart_transport_error",
                    "Storefront could not reach Cart.Api."),
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (JsonException exception)
        {
            dependency.Complete(
                outcome: "invalid_response",
                notes:
                [
                    $"exception:{exception.GetType().Name}"
                ]);

            trace.SetErrorCode("cart_invalid_response");
            trace.AddNote("Storefront received an invalid JSON response from Cart.Api.");
            trace.RecordInstantStage(
                "cart_call_completed",
                outcome: "invalid_response",
                metadata: new Dictionary<string, string?>
                {
                    ["userId"] = request.UserId,
                    ["productId"] = request.ProductId,
                    ["error"] = "cart_invalid_response"
                });
            trace.RecordInstantStage(
                "response_sent",
                outcome: "upstream_failure",
                metadata: new Dictionary<string, string?>
                {
                    ["statusCode"] = StatusCodes.Status502BadGateway.ToString(),
                    ["error"] = "cart_invalid_response",
                    ["userId"] = request.UserId,
                    ["productId"] = request.ProductId,
                    ["sessionKey"] = session.SessionKey
                });

            return Results.Json(
                CreateCartFailureResponse(
                    trace,
                    request.UserId,
                    request.ProductId,
                    null,
                    "cart_invalid_response",
                    "Storefront received an invalid JSON response from Cart.Api."),
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    trace.RecordInstantStage(
        "cart_call_completed",
        outcome: ToCartStageOutcome(cartResult.Outcome),
        metadata: new Dictionary<string, string?>
        {
            ["userId"] = request.UserId,
            ["productId"] = request.ProductId,
            ["statusCode"] = cartResult.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["error"] = cartResult.ErrorCode,
            ["cartTraceId"] = cartResult.CartRequest?.TraceId,
            ["sessionKey"] = session.SessionKey
        });

    switch (cartResult.Outcome)
    {
        case CartClientOutcome.Success:
            string? contractViolation = ValidateAddToCartContract(request, cartResult.Cart!);

            if (contractViolation is not null)
            {
                trace.SetErrorCode("cart_contract_violation");
                trace.AddNote(contractViolation);
                trace.RecordInstantStage(
                    "response_sent",
                    outcome: "contract_failure",
                    metadata: new Dictionary<string, string?>
                    {
                        ["statusCode"] = StatusCodes.Status502BadGateway.ToString(),
                        ["error"] = "cart_contract_violation",
                        ["userId"] = request.UserId,
                        ["productId"] = request.ProductId,
                        ["cartTraceId"] = cartResult.CartRequest?.TraceId,
                        ["sessionKey"] = session.SessionKey
                    });

                return Results.Json(
                    CreateCartFailureResponse(
                        trace,
                        request.UserId,
                        request.ProductId,
                        cartResult.CartRequest,
                        "cart_contract_violation",
                        contractViolation),
                    statusCode: StatusCodes.Status502BadGateway);
            }

            trace.MarkContractSatisfied();
            trace.AddNote("Storefront forwarded add-to-cart to Cart.Api and validated the returned cart state.");
            trace.RecordInstantStage(
                "response_sent",
                outcome: "success",
                metadata: new Dictionary<string, string?>
                {
                    ["statusCode"] = StatusCodes.Status200OK.ToString(),
                    ["userId"] = request.UserId,
                    ["productId"] = request.ProductId,
                    ["totalQuantity"] = cartResult.Cart!.TotalQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["cartTraceId"] = cartResult.CartRequest?.TraceId,
                    ["sessionKey"] = session.SessionKey
                });

            return Results.Ok(CreateCartMutationResponse(trace, cartResult.Cart!, source: "cart-api"));

        case CartClientOutcome.DomainFailure:
            trace.MarkContractSatisfied();
            trace.SetErrorCode(cartResult.ErrorCode);
            trace.AddNote("Cart.Api returned an explicit business failure to Storefront.");
            trace.RecordInstantStage(
                "response_sent",
                outcome: cartResult.StatusCode == StatusCodes.Status404NotFound ? "not_found" : "validation_failed",
                metadata: new Dictionary<string, string?>
                {
                    ["statusCode"] = cartResult.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["error"] = cartResult.ErrorCode,
                    ["userId"] = request.UserId,
                    ["productId"] = request.ProductId,
                    ["cartTraceId"] = cartResult.CartRequest?.TraceId,
                    ["sessionKey"] = session.SessionKey
                });

            return Results.Json(
                CreateCartFailureResponse(
                    trace,
                    request.UserId,
                    request.ProductId,
                    cartResult.CartRequest,
                    cartResult.ErrorCode ?? "cart_domain_failure",
                    cartResult.ErrorDetail ?? "Cart.Api rejected the add-to-cart request."),
                statusCode: cartResult.StatusCode);

        default:
            trace.SetErrorCode(cartResult.ErrorCode ?? "cart_unavailable");
            trace.AddNote("Cart.Api returned a non-success status to Storefront.");
            trace.RecordInstantStage(
                "response_sent",
                outcome: "upstream_failure",
                metadata: new Dictionary<string, string?>
                {
                    ["statusCode"] = StatusCodes.Status502BadGateway.ToString(),
                    ["error"] = cartResult.ErrorCode ?? "cart_unavailable",
                    ["userId"] = request.UserId,
                    ["productId"] = request.ProductId,
                    ["sessionKey"] = session.SessionKey
                });

            return Results.Json(
                CreateCartFailureResponse(
                    trace,
                    request.UserId,
                    request.ProductId,
                    cartResult.CartRequest,
                    cartResult.ErrorCode ?? "cart_unavailable",
                    "Cart.Api returned a non-success status to Storefront."),
                statusCode: StatusCodes.Status502BadGateway);
    }
})
    .WithOperationContract(BusinessOperationContracts.AddItemToCart);

app.MapPost("/checkout", async (
    string? mode,
    [FromBody] StorefrontCheckoutRequest request,
    [FromHeader(Name = LabHeaderNames.IdempotencyKey)] string? idempotencyKey,
    HttpContext httpContext,
    EnvironmentLayout layout,
    Microsoft.Extensions.Options.IOptions<ServiceEndpointOptions> serviceEndpointOptions,
    IRegionNetworkEnvelopePolicy regionNetworkEnvelopePolicy,
    IOrderCheckoutClient orderCheckoutClient,
    IRequestTraceContextAccessor traceAccessor,
    CancellationToken cancellationToken) =>
{
    RequestTraceContext trace = GetRequiredTraceContext(traceAccessor);
    StorefrontSessionKeyResolution session = httpContext.GetRequiredStorefrontSessionKeyResolution();
    bool debugTelemetryRequested = IsDebugTelemetryRequested(httpContext.Request);
    RegionNetworkEnvelope orderNetworkEnvelope = regionNetworkEnvelopePolicy.Resolve(serviceEndpointOptions.Value.OrderRegion);

    if (!string.IsNullOrWhiteSpace(idempotencyKey))
    {
        httpContext.Response.Headers[LabHeaderNames.IdempotencyKey] = idempotencyKey;
    }

    if (!CheckoutExecutionModes.TryParse(mode, out string normalizedMode))
    {
        trace.SetOperationContract(BusinessOperationContracts.StorefrontCheckout);
        trace.SetUserId(request.UserId);
        trace.RecordInstantStage(
            "request_received",
            metadata: new Dictionary<string, string?>
            {
                ["userId"] = request.UserId,
                ["checkoutMode"] = NormalizeOptionalText(mode),
                ["idempotencyKey"] = NormalizeOptionalText(idempotencyKey),
                ["paymentMode"] = NormalizeOptionalText(request.PaymentMode),
                ["paymentCallbackUrl"] = NormalizeOptionalText(request.PaymentCallbackUrl),
                ["sessionKey"] = session.SessionKey,
                ["sessionKeySource"] = session.Source,
                ["sessionCookieIssued"] = session.CookieIssued.ToString().ToLowerInvariant(),
                ["debugTelemetryRequested"] = debugTelemetryRequested.ToString().ToLowerInvariant()
            });
        trace.MarkContractSatisfied();
        trace.SetErrorCode("invalid_checkout_mode");
        trace.AddNote("Storefront rejected checkout because the requested mode was not recognized.");
        trace.RecordInstantStage(
            "response_sent",
            outcome: "validation_failed",
            metadata: new Dictionary<string, string?>
            {
                ["statusCode"] = StatusCodes.Status400BadRequest.ToString(),
                ["error"] = "invalid_checkout_mode",
                ["userId"] = request.UserId,
                ["sessionKey"] = session.SessionKey
            });

        return Results.BadRequest(CreateCheckoutFailureResponse(
            trace,
            request.UserId,
            NormalizeOptionalText(idempotencyKey),
            NormalizeOptionalText(request.PaymentMode),
            null,
            null,
            source: "storefront-api",
            contractSatisfied: true,
            error: "invalid_checkout_mode",
            detail: $"Checkout mode '{NormalizeOptionalText(mode) ?? string.Empty}' is not supported."));
    }

    trace.SetOperationContract(
        CheckoutExecutionModes.IsAsync(normalizedMode)
            ? BusinessOperationContracts.StorefrontCheckoutAsync
            : BusinessOperationContracts.StorefrontCheckoutSync);
    trace.SetUserId(request.UserId);
    trace.RecordInstantStage(
        "request_received",
        metadata: new Dictionary<string, string?>
        {
            ["userId"] = request.UserId,
            ["checkoutMode"] = normalizedMode,
            ["idempotencyKey"] = NormalizeOptionalText(idempotencyKey),
            ["paymentMode"] = NormalizeOptionalText(request.PaymentMode),
            ["paymentCallbackUrl"] = NormalizeOptionalText(request.PaymentCallbackUrl),
            ["sessionKey"] = session.SessionKey,
            ["sessionKeySource"] = session.Source,
            ["sessionCookieIssued"] = session.CookieIssued.ToString().ToLowerInvariant(),
            ["debugTelemetryRequested"] = debugTelemetryRequested.ToString().ToLowerInvariant()
        });

    if (string.IsNullOrWhiteSpace(request.UserId))
    {
        trace.MarkContractSatisfied();
        trace.SetErrorCode("invalid_user_id");
        trace.RecordInstantStage(
            "response_sent",
            outcome: "validation_failed",
            metadata: new Dictionary<string, string?>
            {
                ["statusCode"] = StatusCodes.Status400BadRequest.ToString(),
                ["error"] = "invalid_user_id",
                ["sessionKey"] = session.SessionKey
            });

        return Results.BadRequest(CreateCheckoutFailureResponse(
            trace,
            request.UserId,
            NormalizeOptionalText(idempotencyKey),
            NormalizeOptionalText(request.PaymentMode),
            normalizedMode,
            null,
            source: "storefront-api",
            contractSatisfied: true,
            error: "invalid_user_id",
            detail: "A non-empty user identifier is required."));
    }

    if (string.IsNullOrWhiteSpace(idempotencyKey))
    {
        trace.MarkContractSatisfied();
        trace.SetErrorCode("missing_idempotency_key");
        trace.RecordInstantStage(
            "response_sent",
            outcome: "validation_failed",
            metadata: new Dictionary<string, string?>
            {
                ["statusCode"] = StatusCodes.Status400BadRequest.ToString(),
                ["error"] = "missing_idempotency_key",
                ["userId"] = request.UserId,
                ["sessionKey"] = session.SessionKey
            });

        return Results.BadRequest(CreateCheckoutFailureResponse(
            trace,
            request.UserId,
            null,
            NormalizeOptionalText(request.PaymentMode),
            normalizedMode,
            null,
            source: "storefront-api",
            contractSatisfied: true,
            error: "missing_idempotency_key",
            detail: $"The '{LabHeaderNames.IdempotencyKey}' header is required for checkout."));
    }

    trace.RecordInstantStage(
        "checkout_call_started",
        metadata: new Dictionary<string, string?>
        {
            ["userId"] = request.UserId,
            ["checkoutMode"] = normalizedMode,
            ["route"] = "/orders/checkout",
            ["sessionKey"] = session.SessionKey
        });

    OrderCheckoutClientResult orderResult;

    using (RequestTraceContext.DependencyCallScope dependency = trace.BeginDependencyCall(
        dependencyName: "order-api",
        route: "/orders/checkout",
        region: orderNetworkEnvelope.TargetRegion,
        metadata: DependencyCallNetworkMetadata.Create(orderNetworkEnvelope),
        notes:
        [
            $"operation:{BusinessOperationContracts.OrderCheckout.OperationName}",
            $"checkoutMode={normalizedMode}"
        ]))
    {
        try
        {
            using HttpResponseMessage response = await orderCheckoutClient.CheckoutAsync(
                request,
                normalizedMode,
                idempotencyKey,
                trace.RunId,
                trace.CorrelationId ?? trace.RequestId,
                debugTelemetryRequested,
                cancellationToken);

            orderResult = await OrderCheckoutResponseParser.ReadAsync(response, cancellationToken);

            dependency.Complete(
                statusCode: orderResult.StatusCode,
                outcome: ToCheckoutDependencyOutcome(orderResult.Outcome),
                notes: CreateDependencyNotes(orderResult.ErrorCode));
        }
        catch (HttpRequestException exception)
        {
            dependency.Complete(
                outcome: "transport_error",
                notes:
                [
                    $"exception:{exception.GetType().Name}"
                ]);

            trace.SetErrorCode("order_transport_error");
            trace.AddNote("Storefront could not reach Order.Api.");
            trace.RecordInstantStage(
                "checkout_call_completed",
                outcome: "transport_error",
                metadata: new Dictionary<string, string?>
                {
                    ["userId"] = request.UserId,
                    ["checkoutMode"] = normalizedMode,
                    ["error"] = "order_transport_error",
                    ["sessionKey"] = session.SessionKey
                });
            trace.RecordInstantStage(
                "response_sent",
                outcome: "upstream_failure",
                metadata: new Dictionary<string, string?>
                {
                    ["statusCode"] = StatusCodes.Status502BadGateway.ToString(),
                    ["error"] = "order_transport_error",
                    ["userId"] = request.UserId,
                    ["checkoutMode"] = normalizedMode,
                    ["sessionKey"] = session.SessionKey
                });

            return Results.Json(
                CreateCheckoutFailureResponse(
                    trace,
                    request.UserId,
                    NormalizeOptionalText(idempotencyKey),
                    NormalizeOptionalText(request.PaymentMode),
                    normalizedMode,
                    null,
                    source: "order-api",
                    contractSatisfied: false,
                    error: "order_transport_error",
                    detail: "Storefront could not reach Order.Api."),
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (JsonException exception)
        {
            dependency.Complete(
                outcome: "invalid_response",
                notes:
                [
                    $"exception:{exception.GetType().Name}"
                ]);

            trace.SetErrorCode("order_invalid_response");
            trace.AddNote("Storefront received an invalid JSON response from Order.Api.");
            trace.RecordInstantStage(
                "checkout_call_completed",
                outcome: "invalid_response",
                metadata: new Dictionary<string, string?>
                {
                    ["userId"] = request.UserId,
                    ["checkoutMode"] = normalizedMode,
                    ["error"] = "order_invalid_response",
                    ["sessionKey"] = session.SessionKey
                });
            trace.RecordInstantStage(
                "response_sent",
                outcome: "upstream_failure",
                metadata: new Dictionary<string, string?>
                {
                    ["statusCode"] = StatusCodes.Status502BadGateway.ToString(),
                    ["error"] = "order_invalid_response",
                    ["userId"] = request.UserId,
                    ["checkoutMode"] = normalizedMode,
                    ["sessionKey"] = session.SessionKey
                });

            return Results.Json(
                CreateCheckoutFailureResponse(
                    trace,
                    request.UserId,
                    NormalizeOptionalText(idempotencyKey),
                    NormalizeOptionalText(request.PaymentMode),
                    normalizedMode,
                    null,
                    source: "order-api",
                    contractSatisfied: false,
                    error: "order_invalid_response",
                    detail: "Storefront received an invalid JSON response from Order.Api."),
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    trace.RecordInstantStage(
        "checkout_call_completed",
        outcome: ToCheckoutStageOutcome(orderResult.Outcome),
        metadata: new Dictionary<string, string?>
        {
            ["userId"] = request.UserId,
            ["checkoutMode"] = normalizedMode,
            ["statusCode"] = orderResult.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["error"] = orderResult.ErrorCode,
            ["orderTraceId"] = orderResult.OrderRequest?.TraceId,
            ["sessionKey"] = session.SessionKey
        });

    switch (orderResult.Outcome)
    {
        case OrderCheckoutClientOutcome.Success:
            string? contractViolation = ValidateCheckoutContract(request, normalizedMode, orderResult.Order!);

            if (contractViolation is not null)
            {
                trace.SetErrorCode("order_contract_violation");
                trace.AddNote(contractViolation);
                trace.RecordInstantStage(
                    "response_sent",
                    outcome: "contract_failure",
                    metadata: new Dictionary<string, string?>
                    {
                        ["statusCode"] = StatusCodes.Status502BadGateway.ToString(),
                        ["error"] = "order_contract_violation",
                        ["userId"] = request.UserId,
                        ["checkoutMode"] = normalizedMode,
                        ["orderTraceId"] = orderResult.OrderRequest?.TraceId,
                        ["sessionKey"] = session.SessionKey
                    });

                return Results.Json(
                    CreateCheckoutFailureResponse(
                        trace,
                        request.UserId,
                        NormalizeOptionalText(idempotencyKey),
                        NormalizeOptionalText(request.PaymentMode),
                        normalizedMode,
                        orderResult.OrderRequest,
                        source: "order-api",
                        contractSatisfied: false,
                        error: "order_contract_violation",
                        detail: contractViolation),
                    statusCode: StatusCodes.Status502BadGateway);
            }

            if (orderResult.Order!.ContractSatisfied)
            {
                trace.MarkContractSatisfied();
            }

            if (!string.IsNullOrWhiteSpace(orderResult.Order.PaymentErrorCode))
            {
                trace.SetErrorCode(orderResult.Order.PaymentErrorCode);
            }

            trace.AddNote(
                CheckoutExecutionModes.IsAsync(normalizedMode)
                    ? "Storefront accepted checkout at the user-visible boundary while payment confirmation moved to Worker."
                    : "Storefront waited for Order.Api to complete synchronous checkout before closing the user-visible boundary.");

            trace.RecordInstantStage(
                "response_sent",
                outcome: DetermineCheckoutResponseOutcome(orderResult.Order),
                metadata: new Dictionary<string, string?>
                {
                    ["statusCode"] = orderResult.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["orderId"] = orderResult.Order.OrderId,
                    ["paymentId"] = orderResult.Order.PaymentId,
                    ["paymentStatus"] = orderResult.Order.PaymentStatus,
                    ["checkoutMode"] = orderResult.Order.CheckoutMode,
                    ["backgroundJobId"] = orderResult.Order.BackgroundJobId,
                    ["orderTraceId"] = orderResult.OrderRequest?.TraceId,
                    ["sessionKey"] = session.SessionKey
                });

            return Results.Json(
                CreateCheckoutResponse(trace, orderResult.Order, source: "order-api"),
                statusCode: orderResult.StatusCode);

        case OrderCheckoutClientOutcome.DomainFailure:
            trace.MarkContractSatisfied();
            if (!string.IsNullOrWhiteSpace(orderResult.ErrorCode))
            {
                trace.SetErrorCode(orderResult.ErrorCode);
            }

            trace.AddNote("Order.Api returned an explicit business failure to Storefront.");
            trace.RecordInstantStage(
                "response_sent",
                outcome: orderResult.StatusCode == StatusCodes.Status404NotFound ? "not_found" : "domain_failure",
                metadata: new Dictionary<string, string?>
                {
                    ["statusCode"] = orderResult.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["error"] = orderResult.ErrorCode,
                    ["userId"] = request.UserId,
                    ["checkoutMode"] = normalizedMode,
                    ["orderTraceId"] = orderResult.OrderRequest?.TraceId,
                    ["sessionKey"] = session.SessionKey
                });

            return Results.Json(
                CreateCheckoutFailureResponse(
                    trace,
                    request.UserId,
                    NormalizeOptionalText(idempotencyKey),
                    NormalizeOptionalText(request.PaymentMode),
                    normalizedMode,
                    orderResult.OrderRequest,
                    source: "order-api",
                    contractSatisfied: true,
                    error: orderResult.ErrorCode ?? "order_domain_failure",
                    detail: orderResult.ErrorDetail ?? "Order.Api rejected the checkout request."),
                statusCode: orderResult.StatusCode);

        default:
            trace.SetErrorCode(orderResult.ErrorCode ?? "order_unavailable");
            trace.AddNote("Order.Api returned a non-success status to Storefront.");
            trace.RecordInstantStage(
                "response_sent",
                outcome: "upstream_failure",
                metadata: new Dictionary<string, string?>
                {
                    ["statusCode"] = StatusCodes.Status502BadGateway.ToString(),
                    ["error"] = orderResult.ErrorCode ?? "order_unavailable",
                    ["userId"] = request.UserId,
                    ["checkoutMode"] = normalizedMode,
                    ["sessionKey"] = session.SessionKey
                });

            return Results.Json(
                CreateCheckoutFailureResponse(
                    trace,
                    request.UserId,
                    NormalizeOptionalText(idempotencyKey),
                    NormalizeOptionalText(request.PaymentMode),
                    normalizedMode,
                    orderResult.OrderRequest,
                    source: "order-api",
                    contractSatisfied: false,
                    error: orderResult.ErrorCode ?? "order_unavailable",
                    detail: "Order.Api returned a non-success status to Storefront."),
                statusCode: StatusCodes.Status502BadGateway);
    }
})
    .AddEndpointFilter<CheckoutTokenBucketRateLimitFilter>()
    .WithOperationContract(BusinessOperationContracts.StorefrontCheckout);

app.MapGet("/orders/{userId}", async (
    string userId,
    string? readSource,
    StorefrontOrderHistoryReadService orderHistoryReadService,
    IRequestTraceContextAccessor traceAccessor,
    CancellationToken cancellationToken) =>
{
    RequestTraceContext trace = GetRequiredTraceContext(traceAccessor);
    trace.SetUserId(userId);
    trace.RecordInstantStage(
        "request_received",
        metadata: new Dictionary<string, string?>
        {
            ["userId"] = userId,
            ["readSource"] = readSource
        });

    if (string.IsNullOrWhiteSpace(userId))
    {
        trace.MarkContractSatisfied();
        trace.SetErrorCode("invalid_user_id");
        trace.RecordInstantStage(
            "response_sent",
            outcome: "validation_failed",
            metadata: new Dictionary<string, string?>
            {
                ["statusCode"] = StatusCodes.Status400BadRequest.ToString(),
                ["error"] = "invalid_user_id"
            });

        return Results.BadRequest(new StorefrontOrderHistoryFailureResponse(
            Error: "invalid_user_id",
            Detail: "A non-empty user identifier is required.",
            ContractSatisfied: true,
            UserId: userId,
            Source: "invalid",
            Freshness: null,
            Request: CreateRequestInfo(trace)));
    }

    if (!OrderHistoryReadSourceParser.TryParse(
            readSource,
            out OrderHistoryReadSource selectedReadSource,
            out string? readSourceValidationError))
    {
        trace.MarkContractSatisfied();
        trace.SetErrorCode("invalid_read_source");
        trace.RecordInstantStage(
            "response_sent",
            outcome: "validation_failed",
            metadata: new Dictionary<string, string?>
            {
                ["statusCode"] = StatusCodes.Status400BadRequest.ToString(),
                ["error"] = "invalid_read_source",
                ["userId"] = userId,
                ["readSource"] = readSource
            });

        return Results.BadRequest(new StorefrontOrderHistoryFailureResponse(
            Error: "invalid_read_source",
            Detail: readSourceValidationError ?? "Read source must be 'local', 'read-model' or 'primary-projection'.",
            ContractSatisfied: true,
            UserId: userId,
            Source: "invalid",
            Freshness: null,
            Request: CreateRequestInfo(trace)));
    }

    string selectedReadSourceText = selectedReadSource.ToText();

    StorefrontOrderHistoryReadResult orderHistoryResult;

    try
    {
        using RequestTraceContext.StageTraceScope stage = trace.BeginStage(
            "order_history_read",
            new Dictionary<string, string?>
            {
                ["userId"] = userId,
                ["requestedReadSource"] = selectedReadSourceText
            });

        orderHistoryResult = await orderHistoryReadService.GetByUserIdAsync(userId, selectedReadSource, cancellationToken);

        stage.Complete(
            orderHistoryResult.Orders.Count == 0 ? "empty" : "loaded",
            RegionalReadPreferenceMetadata.Create(
                orderHistoryResult.ReadPreference,
                new Dictionary<string, string?>
                {
                    ["source"] = orderHistoryResult.ReadPreference.EffectiveReadSource,
                    ["orderCount"] = orderHistoryResult.Orders.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));
    }
    catch (InvalidDataException exception)
    {
        trace.SetErrorCode("order_history_invalid_projection");
        trace.AddNote(exception.Message);
        trace.RecordInstantStage(
            "response_sent",
            outcome: "projection_invalid",
            metadata: new Dictionary<string, string?>
            {
                ["statusCode"] = StatusCodes.Status500InternalServerError.ToString(),
                ["error"] = "order_history_invalid_projection",
                ["userId"] = userId,
                ["source"] = selectedReadSourceText
            });

        return Results.Json(
            new StorefrontOrderHistoryFailureResponse(
                Error: "order_history_invalid_projection",
                Detail: "Storefront could not parse the order-history projection.",
                ContractSatisfied: false,
                UserId: userId,
                Source: selectedReadSourceText,
                Freshness: null,
                Request: CreateRequestInfo(trace)),
            statusCode: StatusCodes.Status500InternalServerError);
    }

    IReadOnlyList<StorefrontOrderHistorySnapshot> orders = orderHistoryResult.Orders;
    StorefrontReadFreshnessInfo freshness = orderHistoryResult.Freshness;
    RegionalReadPreference orderHistoryReadPreference = orderHistoryResult.ReadPreference;
    trace.SetReadSource(freshness.ReadSource);
    trace.SetFreshnessMetrics(
        freshness.ComparedCount,
        freshness.StaleCount,
        freshness.MaxStalenessAgeMs);
    trace.RecordInstantStage("freshness_evaluated", metadata: CreateFreshnessMetadata(freshness));
    trace.MarkContractSatisfied();
    AddOrderHistoryReadSelectionNotes(trace, orderHistoryReadPreference);
    if (string.Equals(orderHistoryReadPreference.EffectiveReadSource, OrderHistoryReadSource.ReadModel.ToText(), StringComparison.Ordinal))
    {
        trace.AddNote("Storefront served order history from the local read model instead of the primary write tables.");
        trace.AddNote("Projection lag is possible until Worker applies order-history projection update jobs.");
    }
    else
    {
        trace.AddNote("Storefront built order history from primary write tables during the request.");
        trace.AddNote("This source avoids read-model lag but does more work on the user-visible path.");
    }
    if (freshness.StaleRead)
    {
        trace.AddNote("Storefront detected that the chosen order-history read source was stale relative to primary visibility.");
    }

    trace.RecordInstantStage(
        "response_sent",
        outcome: "success",
        metadata: RegionalReadPreferenceMetadata.Create(
            orderHistoryReadPreference,
            new Dictionary<string, string?>
            {
                ["statusCode"] = StatusCodes.Status200OK.ToString(),
                ["userId"] = userId,
                ["source"] = orderHistoryReadPreference.EffectiveReadSource,
                ["orderCount"] = orders.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["staleRead"] = freshness.StaleRead.ToString().ToLowerInvariant(),
                ["staleFraction"] = FormatDouble(freshness.StaleFraction),
                ["maxStalenessAgeMs"] = FormatDouble(freshness.MaxStalenessAgeMs),
                ["newestProjectedUtc"] = orders.Count == 0
                    ? null
                    : orders.Max(order => order.ProjectedUtc).ToString("O")
            }));

    return Results.Ok(new StorefrontOrderHistoryResponse(
        UserId: userId,
        Source: orderHistoryReadPreference.EffectiveReadSource,
        Freshness: freshness,
        OrderCount: orders.Count,
        OldestProjectedUtc: orders.Count == 0 ? null : orders.Min(order => order.ProjectedUtc),
        NewestProjectedUtc: orders.Count == 0 ? null : orders.Max(order => order.ProjectedUtc),
        Orders: orders,
        Request: CreateRequestInfo(trace)));
})
    .WithOperationContract(BusinessOperationContracts.OrderHistory);

app.MapGet("/products/{id}", async (
    string id,
    string? cache,
    string? readSource,
    HttpContext httpContext,
    EnvironmentLayout layout,
    Microsoft.Extensions.Options.IOptions<ServiceEndpointOptions> serviceEndpointOptions,
    Microsoft.Extensions.Options.IOptions<RegionOptions> regionOptionsAccessor,
    Microsoft.Extensions.Options.IOptions<RegionalDegradationOptions> regionalDegradationOptionsAccessor,
    IRegionNetworkEnvelopePolicy regionNetworkEnvelopePolicy,
    StorefrontProductDetailCache productCache,
    ICatalogProductClient catalogProductClient,
    IRequestTraceContextAccessor traceAccessor,
    CancellationToken cancellationToken) =>
{
    RequestTraceContext trace = GetRequiredTraceContext(traceAccessor);
    bool debugTelemetryRequested = IsDebugTelemetryRequested(httpContext.Request);
    CatalogDependencyRoutePlan catalogDependencyRoute = CatalogDependencyRouteResolver.Resolve(
        layout.CurrentRegion,
        serviceEndpointOptions.Value,
        regionalDegradationOptionsAccessor.Value,
        regionNetworkEnvelopePolicy);

    trace.RecordInstantStage(
        "request_received",
        metadata: new Dictionary<string, string?>
        {
            ["productId"] = id,
            ["cache"] = cache,
            ["readSource"] = readSource,
            ["debugTelemetryRequested"] = debugTelemetryRequested.ToString().ToLowerInvariant()
        });

    if (string.IsNullOrWhiteSpace(id))
    {
        trace.SetErrorCode("invalid_product_id");
        trace.RecordInstantStage(
            "response_sent",
            outcome: "validation_failed",
            metadata: new Dictionary<string, string?>
            {
                ["statusCode"] = StatusCodes.Status400BadRequest.ToString(),
                ["error"] = "invalid_product_id"
            });

        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["productId"] = ["A non-empty product identifier is required."]
        });
    }

    if (!ProductCacheModeParser.TryParse(cache, out ProductCacheMode cacheMode, out string? cacheValidationError))
    {
        trace.SetErrorCode("invalid_cache_mode");
        trace.RecordInstantStage(
            "response_sent",
            outcome: "validation_failed",
            metadata: new Dictionary<string, string?>
            {
                ["statusCode"] = StatusCodes.Status400BadRequest.ToString(),
                ["error"] = "invalid_cache_mode"
            });

        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["cache"] = [cacheValidationError ?? "Cache mode must be either 'on' or 'off'."]
        });
    }

    if (!ProductReadSourceParser.TryParse(readSource, out ProductReadSource productReadSource, out string? readSourceValidationError))
    {
        trace.SetErrorCode("invalid_read_source");
        trace.RecordInstantStage(
            "response_sent",
            outcome: "validation_failed",
            metadata: new Dictionary<string, string?>
            {
                ["statusCode"] = StatusCodes.Status400BadRequest.ToString(),
                ["error"] = "invalid_read_source"
            });

        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["readSource"] = [readSourceValidationError ?? "Read source must be 'local', 'primary', 'replica-east', or 'replica-west'."]
        });
    }

    string selectedReadSourceText = productReadSource.ToText();
    RegionalReadPreference catalogReadPreference = RegionalProductReadPreferenceResolver.Resolve(
        catalogDependencyRoute.EffectiveTargetRegion,
        regionOptionsAccessor.Value,
        selectedReadSourceText,
        localReplicaAvailable: !regionalDegradationOptionsAccessor.Value.SimulateLocalReplicaUnavailable);

    CatalogProductSnapshot? product = null;
    StorefrontCatalogRequestInfo? catalogRequest = null;
    CatalogDebugTelemetryInfo? catalogDebugTelemetry = null;
    bool cacheHit = false;

    if (cacheMode == ProductCacheMode.On)
    {
        using (RequestTraceContext.StageTraceScope stage = trace.BeginStage(
            "cache_lookup",
            RegionalReadPreferenceMetadata.Create(
                catalogReadPreference,
                new Dictionary<string, string?>
                {
                    ["productId"] = id,
                    ["cacheMode"] = ToCacheModeText(cacheMode),
                    ["cacheNamespace"] = productCache.Scope.NamespaceName,
                    ["cacheRegion"] = productCache.Scope.Region,
                    ["catalogDependencyRegion"] = catalogDependencyRoute.EffectiveTargetRegion,
                    ["catalogDependencyNetworkScope"] = catalogDependencyRoute.NetworkEnvelope.NetworkScope,
                    ["degradedModeApplied"] = catalogDependencyRoute.DegradedModeApplied.ToString().ToLowerInvariant(),
                    ["degradedModeReason"] = catalogDependencyRoute.DegradedReason
                })))
        {
            CacheGetResult<CatalogProductSnapshot> cacheResult = await productCache.GetAsync(catalogReadPreference.EffectiveReadSource, id, cancellationToken);
            cacheHit = cacheResult.Hit;

            stage.Complete(
                cacheResult.Hit ? "hit" : "miss",
                RegionalReadPreferenceMetadata.Create(
                    CreateProductReadPreference(catalogReadPreference, cacheResult.Value?.ReadSource),
                    new Dictionary<string, string?>
                    {
                        ["expiresUtc"] = cacheResult.ExpiresUtc?.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                    }));

            if (cacheResult.Hit)
            {
                product = cacheResult.Value!;
                catalogRequest = product.CatalogRequest;
                catalogDebugTelemetry = product.DebugTelemetry;
                trace.SetReadSource(product.ReadSource);
                trace.SetFreshnessMetrics(
                    product.Freshness.ComparedCount,
                    product.Freshness.StaleCount,
                    product.Freshness.MaxStalenessAgeMs);
                trace.MarkCacheHit();
                trace.RecordInstantStage("freshness_evaluated", metadata: CreateFreshnessMetadata(product.Freshness));
            }
        }
    }
    else
    {
        using RequestTraceContext.StageTraceScope stage = trace.BeginStage(
            "cache_lookup",
            RegionalReadPreferenceMetadata.Create(
                catalogReadPreference,
                new Dictionary<string, string?>
                {
                    ["productId"] = id,
                    ["cacheMode"] = ToCacheModeText(cacheMode),
                    ["cacheNamespace"] = productCache.Scope.NamespaceName,
                    ["cacheRegion"] = productCache.Scope.Region,
                    ["catalogDependencyRegion"] = catalogDependencyRoute.EffectiveTargetRegion,
                    ["catalogDependencyNetworkScope"] = catalogDependencyRoute.NetworkEnvelope.NetworkScope,
                    ["degradedModeApplied"] = catalogDependencyRoute.DegradedModeApplied.ToString().ToLowerInvariant(),
                    ["degradedModeReason"] = catalogDependencyRoute.DegradedReason
                }));
        stage.Complete("bypassed");
    }

    if (product is null)
    {
        trace.RecordInstantStage(
            "catalog_call_started",
            metadata: RegionalReadPreferenceMetadata.Create(
                catalogReadPreference,
                new Dictionary<string, string?>
                {
                    ["productId"] = id,
                    ["cacheMode"] = ToCacheModeText(cacheMode),
                    ["route"] = $"/catalog/products/{id}",
                    ["catalogDependencyRegion"] = catalogDependencyRoute.EffectiveTargetRegion,
                    ["catalogDependencyNetworkScope"] = catalogDependencyRoute.NetworkEnvelope.NetworkScope,
                    ["degradedModeApplied"] = catalogDependencyRoute.DegradedModeApplied.ToString().ToLowerInvariant(),
                    ["degradedModeReason"] = catalogDependencyRoute.DegradedReason
                }));

        CatalogProductClientResult catalogResult;
        using (RequestTraceContext.DependencyCallScope dependency = trace.BeginDependencyCall(
            dependencyName: "catalog-api",
            route: $"/catalog/products/{id}",
            region: catalogDependencyRoute.NetworkEnvelope.TargetRegion,
            metadata: DependencyCallNetworkMetadata.Create(
                catalogDependencyRoute.NetworkEnvelope,
                new Dictionary<string, string?>
                {
                    ["requestedReadSource"] = catalogReadPreference.RequestedReadSource,
                    ["effectiveReadSource"] = catalogReadPreference.EffectiveReadSource,
                    ["readSource"] = catalogReadPreference.EffectiveReadSource,
                    ["readTargetRegion"] = catalogReadPreference.TargetRegion,
                    ["readSelectionScope"] = catalogReadPreference.SelectionScope,
                    ["fallbackApplied"] = catalogReadPreference.FallbackApplied.ToString().ToLowerInvariant(),
                    ["fallbackReason"] = catalogReadPreference.FallbackReason,
                    ["catalogRequestedRegion"] = catalogDependencyRoute.RequestedTargetRegion,
                    ["catalogEffectiveRegion"] = catalogDependencyRoute.EffectiveTargetRegion,
                    ["catalogRequestedBaseUrl"] = catalogDependencyRoute.RequestedBaseUrl,
                    ["catalogEffectiveBaseUrl"] = catalogDependencyRoute.EffectiveBaseUrl,
                    ["degradedModeApplied"] = catalogDependencyRoute.DegradedModeApplied.ToString().ToLowerInvariant(),
                    ["degradedModeReason"] = catalogDependencyRoute.DegradedReason
                }),
            notes: [$"cache-mode:{ToCacheModeText(cacheMode)}"]))
        {
            try
            {
                using HttpResponseMessage response = await catalogProductClient.GetProductAsync(
                    productId: id,
                    runId: trace.RunId,
                    correlationId: trace.CorrelationId ?? trace.RequestId,
                    readSource: productReadSource,
                    debugTelemetryRequested: debugTelemetryRequested,
                    routePlan: catalogDependencyRoute,
                    cancellationToken: cancellationToken);

                catalogResult = await CatalogProductResponseParser.ReadAsync(response, cancellationToken);

                dependency.Complete(
                    statusCode: catalogResult.StatusCode,
                    outcome: ToCatalogDependencyOutcome(catalogResult.Outcome),
                    notes: CreateDependencyNotes(catalogResult.ErrorCode));
            }
            catch (HttpRequestException exception)
            {
                dependency.Complete(
                    outcome: "transport_error",
                    notes: [$"exception:{exception.GetType().Name}"]);

                trace.SetErrorCode("catalog_transport_error");
                trace.AddNote("Storefront could not reach Catalog.Api.");
                trace.RecordInstantStage(
                    "catalog_call_completed",
                    outcome: "transport_error",
                    metadata: RegionalReadPreferenceMetadata.Create(
                        catalogReadPreference,
                        new Dictionary<string, string?>
                        {
                            ["productId"] = id,
                            ["error"] = "catalog_transport_error"
                        }));
                trace.RecordInstantStage(
                    "response_sent",
                    outcome: "upstream_failure",
                    metadata: RegionalReadPreferenceMetadata.Create(
                        catalogReadPreference,
                        new Dictionary<string, string?>
                        {
                            ["statusCode"] = StatusCodes.Status502BadGateway.ToString(),
                            ["error"] = "catalog_transport_error",
                            ["readSource"] = catalogReadPreference.EffectiveReadSource,
                            ["cacheHit"] = "false"
                        }));

                return Results.Json(
                    CreateFailureResponse(
                        trace,
                        id,
                        CreateCacheInfo(cacheMode, false, productCache),
                        catalogReadPreference.EffectiveReadSource,
                        null,
                        null,
                        debugTelemetryRequested,
                        catalogDebugTelemetry,
                        "catalog_transport_error"),
                    statusCode: StatusCodes.Status502BadGateway);
            }
            catch (JsonException exception)
            {
                dependency.Complete(
                    outcome: "invalid_response",
                    notes: [$"exception:{exception.GetType().Name}"]);

                trace.SetErrorCode("catalog_invalid_response");
                trace.AddNote("Storefront received an invalid JSON response from Catalog.Api.");
                trace.RecordInstantStage(
                    "catalog_call_completed",
                    outcome: "invalid_response",
                    metadata: RegionalReadPreferenceMetadata.Create(
                        catalogReadPreference,
                        new Dictionary<string, string?>
                        {
                            ["productId"] = id,
                            ["error"] = "catalog_invalid_response"
                        }));
                trace.RecordInstantStage(
                    "response_sent",
                    outcome: "upstream_failure",
                    metadata: RegionalReadPreferenceMetadata.Create(
                        catalogReadPreference,
                        new Dictionary<string, string?>
                        {
                            ["statusCode"] = StatusCodes.Status502BadGateway.ToString(),
                            ["error"] = "catalog_invalid_response",
                            ["readSource"] = catalogReadPreference.EffectiveReadSource,
                            ["cacheHit"] = "false"
                        }));

                return Results.Json(
                    CreateFailureResponse(
                        trace,
                        id,
                        CreateCacheInfo(cacheMode, false, productCache),
                        catalogReadPreference.EffectiveReadSource,
                        null,
                        null,
                        debugTelemetryRequested,
                        catalogDebugTelemetry,
                        "catalog_invalid_response"),
                    statusCode: StatusCodes.Status502BadGateway);
            }
        }

        trace.RecordInstantStage(
            "catalog_call_completed",
            outcome: ToCatalogStageOutcome(catalogResult.Outcome),
            metadata: RegionalReadPreferenceMetadata.Create(
                CreateProductReadPreference(
                    catalogReadPreference,
                    catalogResult.Product?.ReadSource ?? catalogResult.Freshness?.ReadSource),
                new Dictionary<string, string?>
                {
                    ["productId"] = id,
                    ["readSource"] = catalogResult.Product?.ReadSource ?? catalogResult.Freshness?.ReadSource ?? catalogReadPreference.EffectiveReadSource,
                    ["statusCode"] = catalogResult.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["error"] = catalogResult.ErrorCode,
                    ["catalogTraceId"] = catalogResult.CatalogRequest?.TraceId
                }));

        catalogRequest = catalogResult.CatalogRequest;
        catalogDebugTelemetry = catalogResult.DebugTelemetry;
        if (catalogResult.Freshness is not null)
        {
            trace.SetReadSource(catalogResult.Freshness.ReadSource);
            trace.SetFreshnessMetrics(
                catalogResult.Freshness.ComparedCount,
                catalogResult.Freshness.StaleCount,
                catalogResult.Freshness.MaxStalenessAgeMs);
            trace.RecordInstantStage("freshness_evaluated", metadata: CreateFreshnessMetadata(catalogResult.Freshness));
        }

        switch (catalogResult.Outcome)
        {
            case CatalogProductClientOutcome.Success:
                product = catalogResult.Product!;
                if (cacheMode == ProductCacheMode.On)
                {
                    await productCache.SetAsync(product.ReadSource, product, cancellationToken);
                }

                break;

            case CatalogProductClientOutcome.NotFound:
                trace.MarkContractSatisfied();
                trace.SetErrorCode(catalogResult.ErrorCode ?? "product_not_found");
                trace.AddNote("Catalog.Api returned an explicit not-found result.");
                AddProductReadSelectionNotes(
                    trace,
                    CreateProductReadPreference(catalogReadPreference, catalogResult.Freshness?.ReadSource));
                trace.RecordInstantStage(
                    "response_sent",
                    outcome: "not_found",
                    metadata: RegionalReadPreferenceMetadata.Create(
                        CreateProductReadPreference(catalogReadPreference, catalogResult.Freshness?.ReadSource),
                        new Dictionary<string, string?>
                        {
                            ["statusCode"] = StatusCodes.Status404NotFound.ToString(),
                            ["error"] = catalogResult.ErrorCode ?? "product_not_found",
                            ["readSource"] = catalogResult.Freshness?.ReadSource ?? catalogReadPreference.EffectiveReadSource,
                            ["staleRead"] = catalogResult.Freshness?.StaleRead.ToString().ToLowerInvariant(),
                            ["staleFraction"] = FormatDouble(catalogResult.Freshness?.StaleFraction),
                            ["maxStalenessAgeMs"] = FormatDouble(catalogResult.Freshness?.MaxStalenessAgeMs),
                            ["cacheHit"] = "false"
                        }));

                return Results.NotFound(
                    CreateNotFoundResponse(
                        trace,
                        id,
                        CreateCacheInfo(cacheMode, false, productCache),
                        catalogResult.Freshness?.ReadSource ?? catalogReadPreference.EffectiveReadSource,
                        catalogResult.Freshness,
                        catalogRequest,
                        debugTelemetryRequested,
                        catalogDebugTelemetry,
                        catalogResult.ErrorCode ?? "product_not_found"));

            default:
                trace.SetErrorCode(catalogResult.ErrorCode ?? "catalog_unavailable");
                trace.AddNote("Catalog.Api returned a non-success status to Storefront.");
                trace.RecordInstantStage(
                    "response_sent",
                    outcome: "upstream_failure",
                    metadata: RegionalReadPreferenceMetadata.Create(
                        CreateProductReadPreference(catalogReadPreference, catalogResult.Freshness?.ReadSource),
                        new Dictionary<string, string?>
                        {
                            ["statusCode"] = StatusCodes.Status502BadGateway.ToString(),
                            ["error"] = catalogResult.ErrorCode ?? "catalog_unavailable",
                            ["readSource"] = catalogResult.Freshness?.ReadSource ?? catalogReadPreference.EffectiveReadSource,
                            ["cacheHit"] = "false"
                        }));

                return Results.Json(
                    CreateFailureResponse(
                        trace,
                        id,
                        CreateCacheInfo(cacheMode, false, productCache),
                        catalogResult.Freshness?.ReadSource ?? catalogReadPreference.EffectiveReadSource,
                        catalogResult.Freshness,
                        catalogRequest,
                        debugTelemetryRequested,
                        catalogDebugTelemetry,
                        catalogResult.ErrorCode ?? "catalog_unavailable"),
                    statusCode: StatusCodes.Status502BadGateway);
        }
    }

    trace.MarkContractSatisfied();
    AddCatalogDependencyRouteNotes(trace, catalogDependencyRoute);
    AddProductReadSelectionNotes(trace, CreateProductReadPreference(catalogReadPreference, product.ReadSource));
    trace.AddNote(cacheHit
        ? $"Storefront served the product page from its in-memory cache with underlying read source '{product.ReadSource}'."
        : $"Storefront fetched the product page from Catalog.Api using read source '{product.ReadSource}' and served it at the user-visible boundary.");
    if (product.Freshness.StaleRead)
    {
        trace.AddNote("Storefront detected that the product response was stale relative to primary visibility.");
    }

    trace.RecordInstantStage(
        "response_sent",
        outcome: "success",
        metadata: RegionalReadPreferenceMetadata.Create(
            CreateProductReadPreference(catalogReadPreference, product.ReadSource),
            new Dictionary<string, string?>
            {
                ["statusCode"] = StatusCodes.Status200OK.ToString(),
                ["productId"] = product.ProductId,
                ["readSource"] = product.ReadSource,
                ["staleRead"] = product.Freshness.StaleRead.ToString().ToLowerInvariant(),
                ["staleFraction"] = FormatDouble(product.Freshness.StaleFraction),
                ["maxStalenessAgeMs"] = FormatDouble(product.Freshness.MaxStalenessAgeMs),
                ["cacheHit"] = cacheHit.ToString().ToLowerInvariant(),
                ["catalogTraceId"] = catalogRequest?.TraceId
            }));

    return Results.Ok(
        CreateProductPageResponse(
            trace,
            product,
            CreateCacheInfo(cacheMode, cacheHit, productCache),
            debugTelemetryRequested,
            catalogDebugTelemetry,
            source: cacheHit ? "storefront-cache" : "catalog-api",
            readSource: product.ReadSource));
})
    .WithOperationContract(BusinessOperationContracts.ProductPage);

app.MapGet("/cpu", (
    int? workFactor,
    int? iterations,
    IRequestTraceContextAccessor traceAccessor) =>
{
    RequestTraceContext trace = GetRequiredTraceContext(traceAccessor);
    int effectiveWorkFactor = workFactor ?? 1;
    int effectiveIterations = iterations ?? 200;

    trace.RecordInstantStage(
        "request_received",
        metadata: new Dictionary<string, string?>
        {
            ["workFactor"] = effectiveWorkFactor.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["iterations"] = effectiveIterations.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });

    if (!CpuWorkSimulator.TryValidate(effectiveWorkFactor, effectiveIterations, out string? validationError))
    {
        trace.AddNote("CPU endpoint rejected invalid work parameters.");
        trace.RecordInstantStage(
            "response_sent",
            outcome: "validation_failed",
            metadata: new Dictionary<string, string?>
            {
                ["error"] = validationError
            });

        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["cpuParameters"] = [validationError ?? "Invalid CPU parameters."]
        });
    }

    trace.RecordInstantStage(
        "cpu_work_started",
        metadata: new Dictionary<string, string?>
        {
            ["workFactor"] = effectiveWorkFactor.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["iterations"] = effectiveIterations.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });

    CpuWorkResult result = CpuWorkSimulator.Execute(effectiveWorkFactor, effectiveIterations);

    trace.RecordInstantStage(
        "cpu_work_completed",
        outcome: "success",
        metadata: new Dictionary<string, string?>
        {
            ["checksum"] = result.Checksum,
            ["totalMixOperations"] = result.TotalMixOperations.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });

    trace.MarkContractSatisfied();
    trace.AddNote("CPU lab endpoint completed deterministic CPU work.");
    trace.RecordInstantStage(
        "response_sent",
        outcome: "success",
        metadata: new Dictionary<string, string?>
        {
            ["checksum"] = result.Checksum
        });

    return Results.Ok(new
    {
        workFactor = result.WorkFactor,
        iterations = result.Iterations,
        totalMixOperations = result.TotalMixOperations,
        checksum = result.Checksum,
        request = new
        {
            trace.RunId,
            trace.TraceId,
            trace.RequestId,
            trace.CorrelationId
        }
    });
})
    .WithOperationContract(BusinessOperationContracts.CpuBoundLab);

app.MapGet("/io", async (
    int? delayMs,
    int? jitterMs,
    HttpContext httpContext,
    IRequestTraceContextAccessor traceAccessor) =>
{
    RequestTraceContext trace = GetRequiredTraceContext(traceAccessor);
    int effectiveDelayMs = delayMs ?? 50;
    int effectiveJitterMs = jitterMs ?? 0;

    trace.RecordInstantStage(
        "request_received",
        metadata: new Dictionary<string, string?>
        {
            ["delayMs"] = effectiveDelayMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["jitterMs"] = effectiveJitterMs.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });

    if (!IoWaitSimulator.TryValidate(effectiveDelayMs, effectiveJitterMs, out string? validationError))
    {
        trace.AddNote("I/O endpoint rejected invalid wait parameters.");
        trace.RecordInstantStage(
            "response_sent",
            outcome: "validation_failed",
            metadata: new Dictionary<string, string?>
            {
                ["error"] = validationError
            });

        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["ioParameters"] = [validationError ?? "Invalid I/O parameters."]
        });
    }

    IoWaitPlan plan = IoWaitSimulator.CreatePlan(effectiveDelayMs, effectiveJitterMs, trace.TraceId);

    trace.RecordInstantStage(
        "downstream_wait_started",
        metadata: new Dictionary<string, string?>
        {
            ["delayMs"] = plan.DelayMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["jitterMs"] = plan.JitterMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["jitterOffsetMs"] = plan.JitterOffsetMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["appliedDelayMs"] = plan.AppliedDelayMs.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });

    using (RequestTraceContext.StageTraceScope stage = trace.BeginStage(
        "downstream_wait",
        new Dictionary<string, string?>
        {
            ["delayMs"] = plan.DelayMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["jitterMs"] = plan.JitterMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["jitterOffsetMs"] = plan.JitterOffsetMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["appliedDelayMs"] = plan.AppliedDelayMs.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }))
    {
        await IoWaitSimulator.WaitAsync(plan, httpContext.RequestAborted);
        stage.Complete(
            "success",
            new Dictionary<string, string?>
            {
                ["appliedDelayMs"] = plan.AppliedDelayMs.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
    }

    trace.RecordInstantStage(
        "downstream_wait_completed",
        outcome: "success",
        metadata: new Dictionary<string, string?>
        {
            ["appliedDelayMs"] = plan.AppliedDelayMs.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });

    trace.MarkContractSatisfied();
    trace.AddNote("I/O lab endpoint completed low-CPU downstream wait simulation.");
    trace.RecordInstantStage(
        "response_sent",
        outcome: "success",
        metadata: new Dictionary<string, string?>
        {
            ["appliedDelayMs"] = plan.AppliedDelayMs.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });

    return Results.Ok(new
    {
        delayMs = plan.DelayMs,
        jitterMs = plan.JitterMs,
        jitterOffsetMs = plan.JitterOffsetMs,
        appliedDelayMs = plan.AppliedDelayMs,
        request = new
        {
            trace.RunId,
            trace.TraceId,
            trace.RequestId,
            trace.CorrelationId
        }
    });
})
    .WithOperationContract(BusinessOperationContracts.IoBoundLab);

app.Run();

static ProductPageResponse CreateProductPageResponse(
    RequestTraceContext trace,
    CatalogProductSnapshot product,
    StorefrontCacheInfo cache,
    bool debugTelemetryRequested,
    CatalogDebugTelemetryInfo? catalogDebugTelemetry,
    string source,
    string readSource) =>
    new(
        ProductId: product.ProductId,
        Name: product.Name,
        Description: product.Description,
        Category: product.Category,
        Price: new StorefrontPriceInfo(
            AmountCents: product.PriceAmountCents,
            CurrencyCode: product.PriceCurrencyCode,
            Display: product.PriceDisplay),
        Inventory: new StorefrontInventoryInfo(
            AvailableQuantity: product.AvailableQuantity,
            ReservedQuantity: product.ReservedQuantity,
            SellableQuantity: product.SellableQuantity,
            StockStatus: product.StockStatus),
        Version: product.Version,
        Source: source,
        ReadSource: readSource,
        Freshness: product.Freshness,
        Cache: cache,
        Catalog: product.CatalogRequest,
        Request: CreateRequestInfo(trace))
    {
        DebugTelemetry = debugTelemetryRequested ? CreateDebugTelemetry(trace, catalogDebugTelemetry) : null
    };

static ProductPageNotFoundResponse CreateNotFoundResponse(
    RequestTraceContext trace,
    string productId,
    StorefrontCacheInfo cache,
    string readSource,
    StorefrontReadFreshnessInfo? freshness,
    StorefrontCatalogRequestInfo? catalogRequest,
    bool debugTelemetryRequested,
    CatalogDebugTelemetryInfo? catalogDebugTelemetry,
    string error) =>
    new(
        Error: error,
        ProductId: productId,
        Source: "catalog-api",
        ReadSource: readSource,
        Freshness: freshness,
        Cache: cache,
        Catalog: catalogRequest,
        Request: CreateRequestInfo(trace))
    {
        DebugTelemetry = debugTelemetryRequested ? CreateDebugTelemetry(trace, catalogDebugTelemetry) : null
    };

static ProductPageFailureResponse CreateFailureResponse(
    RequestTraceContext trace,
    string productId,
    StorefrontCacheInfo cache,
    string readSource,
    StorefrontReadFreshnessInfo? freshness,
    StorefrontCatalogRequestInfo? catalogRequest,
    bool debugTelemetryRequested,
    CatalogDebugTelemetryInfo? catalogDebugTelemetry,
    string error) =>
    new(
        Error: error,
        ProductId: productId,
        Source: "catalog-api",
        ReadSource: readSource,
        Freshness: freshness,
        Cache: cache,
        Catalog: catalogRequest,
        Request: CreateRequestInfo(trace))
    {
        DebugTelemetry = debugTelemetryRequested ? CreateDebugTelemetry(trace, catalogDebugTelemetry) : null
    };

static RegionalReadPreference CreateProductReadPreference(
    RegionalReadPreference readPreference,
    string? effectiveReadSource = null) =>
    string.IsNullOrWhiteSpace(effectiveReadSource)
        ? readPreference
        : readPreference with { EffectiveReadSource = effectiveReadSource.Trim() };

static IReadOnlyDictionary<string, string?> CreateFreshnessMetadata(StorefrontReadFreshnessInfo freshness) =>
    new Dictionary<string, string?>
    {
        ["readSource"] = freshness.ReadSource,
        ["comparedCount"] = freshness.ComparedCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["staleCount"] = freshness.StaleCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["staleRead"] = freshness.StaleRead.ToString().ToLowerInvariant(),
        ["staleFraction"] = FormatDouble(freshness.StaleFraction),
        ["maxStalenessAgeMs"] = FormatDouble(freshness.MaxStalenessAgeMs),
        ["observedVersion"] = freshness.ObservedVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["primaryVersion"] = freshness.PrimaryVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["observedUpdatedUtc"] = freshness.ObservedUpdatedUtc?.ToString("O"),
        ["primaryUpdatedUtc"] = freshness.PrimaryUpdatedUtc?.ToString("O")
    };

static StorefrontCartMutationResponse CreateCartMutationResponse(
    RequestTraceContext trace,
    StorefrontCartSnapshot cart,
    string source) =>
    new(
        CartId: cart.CartId,
        UserId: cart.UserId,
        Region: cart.Region,
        Exists: cart.Exists,
        Status: cart.Status,
        LoadOutcome: cart.LoadOutcome,
        MutationOutcome: cart.MutationOutcome,
        Persisted: cart.Persisted,
        DistinctItemCount: cart.DistinctItemCount,
        TotalQuantity: cart.TotalQuantity,
        TotalPriceCents: cart.TotalPriceCents,
        Source: source,
        Items: cart.Items
            .Select(item => new StorefrontCartItemResponse(
                item.ProductId,
                item.Quantity,
                item.UnitPriceSnapshotCents,
                item.LineSubtotalCents,
                item.AddedUtc))
            .ToArray(),
        Cart: cart.CartRequest,
        Request: CreateRequestInfo(trace));

static StorefrontCartFailureResponse CreateCartFailureResponse(
    RequestTraceContext trace,
    string userId,
    string productId,
    StorefrontCartServiceRequestInfo? cartRequest,
    string error,
    string detail) =>
    new(
        Error: error,
        Detail: detail,
        UserId: userId,
        ProductId: productId,
        Source: "cart-api",
        Cart: cartRequest,
        Request: CreateRequestInfo(trace));

static StorefrontCheckoutResponse CreateCheckoutResponse(
    RequestTraceContext trace,
    StorefrontOrderSnapshot order,
    string source) =>
    new(
        OrderId: order.OrderId,
        Status: order.Status,
        ContractSatisfied: order.ContractSatisfied,
        PaymentId: order.PaymentId,
        PaymentStatus: order.PaymentStatus,
        TotalAmountCents: order.TotalAmountCents,
        UserId: order.UserId,
        CartId: order.CartId,
        Region: order.Region,
        ItemCount: order.ItemCount,
        PaymentMode: order.PaymentMode,
        PaymentProviderReference: order.PaymentProviderReference,
        PaymentOutcome: order.PaymentOutcome,
        PaymentErrorCode: order.PaymentErrorCode,
        CheckoutMode: order.CheckoutMode,
        BackgroundJobId: order.BackgroundJobId,
        Source: source,
        Order: order.OrderRequest,
        Request: CreateRequestInfo(trace));

static StorefrontCheckoutFailureResponse CreateCheckoutFailureResponse(
    RequestTraceContext trace,
    string userId,
    string? idempotencyKey,
    string? paymentMode,
    string? checkoutMode,
    StorefrontOrderRequestInfo? orderRequest,
    string source,
    bool contractSatisfied,
    string error,
    string detail) =>
    new(
        Error: error,
        Detail: detail,
        ContractSatisfied: contractSatisfied,
        UserId: userId,
        IdempotencyKey: idempotencyKey,
        PaymentMode: paymentMode,
        CheckoutMode: checkoutMode,
        Source: source,
        Order: orderRequest,
        Request: CreateRequestInfo(trace));

static StorefrontRequestInfo CreateRequestInfo(RequestTraceContext trace) =>
    new(
        RunId: trace.RunId,
        TraceId: trace.TraceId,
        RequestId: trace.RequestId,
        CorrelationId: trace.CorrelationId);

static StorefrontDebugTelemetryInfo CreateDebugTelemetry(
    RequestTraceContext trace,
    CatalogDebugTelemetryInfo? catalogDebugTelemetry) =>
    new(
        StageTimings: trace.StageTimings
            .Select(stage => new StorefrontDebugStageInfo(
                StageName: stage.StageName,
                ElapsedMs: stage.ElapsedMs,
                Outcome: stage.Outcome,
                Metadata: stage.Metadata))
            .ToArray(),
        DependencyCalls: trace.DependencyCalls
            .Select(call => new StorefrontDebugDependencyInfo(
                DependencyName: call.DependencyName,
                Route: call.Route,
                Region: call.Region,
                ElapsedMs: call.ElapsedMs,
                StatusCode: call.StatusCode,
                Outcome: call.Outcome,
                Notes: call.Notes))
            .ToArray(),
        Notes: trace.Notes.ToArray())
    {
        Catalog = catalogDebugTelemetry
    };

static StorefrontCacheInfo CreateCacheInfo(
    ProductCacheMode cacheMode,
    bool cacheHit,
    StorefrontProductDetailCache productCache) =>
    new(
        Mode: ToCacheModeText(cacheMode),
        Hit: cacheHit,
        NamespaceName: productCache.Scope.NamespaceName,
        Region: productCache.Scope.Region);

static bool IsDebugTelemetryRequested(HttpRequest request)
{
    if (!request.Headers.TryGetValue(LabHeaderNames.DebugTelemetry, out Microsoft.Extensions.Primitives.StringValues values))
    {
        return false;
    }

    return bool.TryParse(values.ToString(), out bool enabled) && enabled;
}

static string EnsureTrailingSlash(string value) =>
    value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";

static string ToCacheModeText(ProductCacheMode cacheMode) =>
    cacheMode == ProductCacheMode.On ? "on" : "off";

static string ToCatalogDependencyOutcome(CatalogProductClientOutcome outcome) =>
    outcome switch
    {
        CatalogProductClientOutcome.Success => "success",
        CatalogProductClientOutcome.NotFound => "not_found",
        _ => "error"
    };

static string ToCatalogStageOutcome(CatalogProductClientOutcome outcome) =>
    outcome switch
    {
        CatalogProductClientOutcome.Success => "success",
        CatalogProductClientOutcome.NotFound => "not_found",
        _ => "upstream_failure"
    };

static string ToCartDependencyOutcome(CartClientOutcome outcome) =>
    outcome switch
    {
        CartClientOutcome.Success => "success",
        CartClientOutcome.DomainFailure => "domain_failure",
        _ => "error"
    };

static string ToCartStageOutcome(CartClientOutcome outcome) =>
    outcome switch
    {
        CartClientOutcome.Success => "success",
        CartClientOutcome.DomainFailure => "domain_failure",
        _ => "upstream_failure"
    };

static string ToCheckoutDependencyOutcome(OrderCheckoutClientOutcome outcome) =>
    outcome switch
    {
        OrderCheckoutClientOutcome.Success => "success",
        OrderCheckoutClientOutcome.DomainFailure => "domain_failure",
        _ => "error"
    };

static string ToCheckoutStageOutcome(OrderCheckoutClientOutcome outcome) =>
    outcome switch
    {
        OrderCheckoutClientOutcome.Success => "success",
        OrderCheckoutClientOutcome.DomainFailure => "domain_failure",
        _ => "upstream_failure"
    };

static string DetermineCheckoutResponseOutcome(StorefrontOrderSnapshot order) =>
    order.Status switch
    {
        "Paid" => "paid",
        "PendingPayment" when string.Equals(order.CheckoutMode, CheckoutExecutionModes.Async, StringComparison.Ordinal) => "accepted_pending",
        "PendingPayment" => "pending_payment",
        "Failed" => "failed",
        "Cancelled" => "cancelled",
        _ => "success"
    };

static string? ValidateCheckoutContract(
    StorefrontCheckoutRequest request,
    string checkoutMode,
    StorefrontOrderSnapshot order)
{
    if (!string.Equals(order.UserId, request.UserId, StringComparison.Ordinal))
    {
        return $"Order.Api returned checkout state for '{order.UserId}' instead of requested user '{request.UserId}'.";
    }

    if (string.IsNullOrWhiteSpace(order.OrderId))
    {
        return "Order.Api returned a success response without an order identifier.";
    }

    if (string.IsNullOrWhiteSpace(order.PaymentId))
    {
        return "Order.Api returned a success response without a payment identifier.";
    }

    if (!string.Equals(order.CheckoutMode, checkoutMode, StringComparison.Ordinal))
    {
        return $"Order.Api returned checkoutMode '{order.CheckoutMode}' instead of requested mode '{checkoutMode}'.";
    }

    if (CheckoutExecutionModes.IsAsync(checkoutMode) &&
        string.Equals(order.Status, "PendingPayment", StringComparison.Ordinal) &&
        string.IsNullOrWhiteSpace(order.BackgroundJobId))
    {
        return "Order.Api returned a pending async checkout response without the background job identifier.";
    }

    return null;
}

static string? ValidateAddToCartContract(
    StorefrontCartMutationRequest request,
    StorefrontCartSnapshot cart)
{
    if (!string.Equals(cart.UserId, request.UserId, StringComparison.Ordinal))
    {
        return $"Cart.Api returned cart state for '{cart.UserId}' instead of requested user '{request.UserId}'.";
    }

    if (!cart.Exists)
    {
        return "Cart.Api reported a missing cart after a successful add-to-cart response.";
    }

    if (!cart.Persisted)
    {
        return "Cart.Api reported a non-persisted cart after a successful add-to-cart response.";
    }

    StorefrontCartItemSnapshot? item = cart.Items.SingleOrDefault(existingItem =>
        string.Equals(existingItem.ProductId, request.ProductId, StringComparison.Ordinal));

    if (item is null)
    {
        return $"Cart.Api did not return the requested product '{request.ProductId}' in the resulting cart.";
    }

    if (item.Quantity < request.Quantity)
    {
        return $"Cart.Api returned quantity {item.Quantity} for '{request.ProductId}', which is less than requested add quantity {request.Quantity}.";
    }

    return cart.MutationOutcome switch
    {
        "added" when item.Quantity != request.Quantity =>
            $"Cart.Api reported mutationOutcome=added but returned quantity {item.Quantity} instead of {request.Quantity}.",
        "accumulated" when item.Quantity <= request.Quantity =>
            $"Cart.Api reported mutationOutcome=accumulated but returned quantity {item.Quantity}, which does not exceed the requested add quantity {request.Quantity}.",
        "added" or "accumulated" => null,
        _ => $"Cart.Api returned unexpected mutationOutcome '{cart.MutationOutcome}' for add-to-cart."
    };
}

static IEnumerable<string> CreateDependencyNotes(string? errorCode)
{
    if (!string.IsNullOrWhiteSpace(errorCode))
    {
        yield return $"error:{errorCode}";
    }
}

static string? FormatDouble(double? value) =>
    value?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

static string? NormalizeOptionalText(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : value.Trim();

static void AddProductReadSelectionNotes(RequestTraceContext trace, RegionalReadPreference readPreference)
{
    if (readPreference.RequestedReadSource == readPreference.EffectiveReadSource)
    {
        trace.AddNote(
            $"Storefront resolved product read source '{readPreference.RequestedReadSource}' as a {readPreference.SelectionScope} read in region '{readPreference.TargetRegion}'.");
        return;
    }

    trace.AddNote(
        $"Storefront fell back from requested product read source '{readPreference.RequestedReadSource}' to '{readPreference.EffectiveReadSource}'.");
    trace.AddNote(
        $"Fallback reason: {readPreference.FallbackReason ?? "unspecified"}; target region '{readPreference.TargetRegion}' via {readPreference.SelectionScope} path.");
}

static void AddCatalogDependencyRouteNotes(RequestTraceContext trace, CatalogDependencyRoutePlan routePlan)
{
    if (!routePlan.DegradedModeApplied)
    {
        return;
    }

    trace.AddNote(
        $"Storefront degraded mode rerouted the Catalog dependency from region '{routePlan.RequestedTargetRegion}' to '{routePlan.EffectiveTargetRegion}'.");
    trace.AddNote(
        $"Degraded-mode reason: {routePlan.DegradedReason ?? "unspecified"}; the product read stayed available through a slower {routePlan.NetworkEnvelope.NetworkScope} path.");
}

static void AddOrderHistoryReadSelectionNotes(RequestTraceContext trace, RegionalReadPreference readPreference)
{
    if (readPreference.RequestedReadSource == readPreference.EffectiveReadSource)
    {
        trace.AddNote(
            $"Storefront resolved order-history read source '{readPreference.RequestedReadSource}' as a {readPreference.SelectionScope} read in region '{readPreference.TargetRegion}'.");
        return;
    }

    trace.AddNote(
        $"Storefront fell back from requested order-history read source '{readPreference.RequestedReadSource}' to '{readPreference.EffectiveReadSource}'.");
    trace.AddNote(
        $"Fallback reason: {readPreference.FallbackReason ?? "unspecified"}; target region '{readPreference.TargetRegion}' via {readPreference.SelectionScope} path.");
}

static RequestTraceContext GetRequiredTraceContext(IRequestTraceContextAccessor accessor) =>
    accessor.Current ?? throw new InvalidOperationException("Request trace context is not available for the current request.");

public partial class Program
{
}
