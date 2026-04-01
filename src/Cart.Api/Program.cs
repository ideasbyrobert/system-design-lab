using Cart.Api.CartState;
using Lab.Persistence;
using Lab.Persistence.DependencyInjection;
using Lab.Shared.Contracts;
using Lab.Shared.Configuration;
using Lab.Shared.Logging;
using Lab.Telemetry.AspNetCore;
using Lab.Telemetry.DependencyInjection;
using Lab.Telemetry.RequestTracing;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLabConfiguration(builder.Configuration, builder.Environment);
builder.Services.AddLabTelemetry();
builder.Services.AddRequestTraceJsonlWriter();
builder.Services.AddPrimaryPersistence();
builder.Services.AddScoped<CartStateService>();
builder.Services.AddProblemDetails();
builder.Logging.AddLabOperationalFileLogging();

var app = builder.Build();
app.LogResolvedLabEnvironment();
await EnsurePrimaryDatabaseReadyAsync(app.Services);

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        ILogger logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Cart.Exceptions");
        IProblemDetailsService problemDetails = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        IExceptionHandlerFeature? exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();

        logger.LogError(exceptionFeature?.Error, "Unhandled exception reached the Cart exception boundary.");

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Unhandled Cart error",
                Detail = "The Cart host hit an unhandled exception while processing the request.",
                Extensions =
                {
                    ["requestId"] = context.TraceIdentifier
                }
            }
        });
    });
});

app.UseRequestTracing();

app.MapGet("/", (
    EnvironmentLayout layout,
    IRequestTraceContextAccessor traceAccessor) =>
{
    RequestTraceContext trace = GetRequiredTraceContext(traceAccessor);
    trace.RecordInstantStage(
        "describe_cart_host",
        metadata: new Dictionary<string, string?>
        {
            ["service"] = layout.ServiceName,
            ["region"] = layout.CurrentRegion
        });

    trace.MarkContractSatisfied();
    trace.AddNote("Cart host-info endpoint completed successfully.");

    return Results.Ok(new CartHostInfoResponse(
        layout.ServiceName,
        layout.CurrentRegion,
        layout.RepositoryRoot,
        CreateRequestInfo(trace)));
})
    .WithOperationContract(BusinessOperationContracts.CartHostInfo);

app.MapPost("/cart/items", async (
    [FromBody] CartItemMutationRequest request,
    CartStateService cartStateService,
    IRequestTraceContextAccessor traceAccessor,
    CancellationToken cancellationToken) =>
{
    RequestTraceContext trace = GetRequiredTraceContext(traceAccessor);
    return await HandleAddAsync(request, trace, cartStateService, cancellationToken);
})
    .WithOperationContract(BusinessOperationContracts.CartAddItem);

app.MapDelete("/cart/items", async (
    [FromBody] CartItemMutationRequest request,
    CartStateService cartStateService,
    IRequestTraceContextAccessor traceAccessor,
    CancellationToken cancellationToken) =>
{
    RequestTraceContext trace = GetRequiredTraceContext(traceAccessor);
    return await HandleRemoveAsync(request, trace, cartStateService, cancellationToken);
})
    .WithOperationContract(BusinessOperationContracts.CartRemoveItem);

app.MapGet("/cart/{userId}", async (
    string userId,
    CartStateService cartStateService,
    IRequestTraceContextAccessor traceAccessor,
    CancellationToken cancellationToken) =>
{
    RequestTraceContext trace = GetRequiredTraceContext(traceAccessor);
    trace.SetUserId(userId);
    trace.RecordInstantStage(
        "request_received",
        metadata: new Dictionary<string, string?>
        {
            ["userId"] = userId
        });

    if (string.IsNullOrWhiteSpace(userId))
    {
        return CreateValidationFailure(
            trace,
            "invalid_user_id",
            "A non-empty user identifier is required.",
            userId,
            productId: null);
    }

    CartLoadResult loadResult;

    using (RequestTraceContext.StageTraceScope stage = trace.BeginStage(
        "cart_loaded_or_created",
        new Dictionary<string, string?>
        {
            ["userId"] = userId
        }))
    {
        loadResult = await cartStateService.LoadCartForReadAsync(userId, cancellationToken);
        stage.Complete(
            loadResult.Succeeded ? loadResult.LoadOutcome : "failed",
            new Dictionary<string, string?>
            {
                ["exists"] = loadResult.Context?.Cart is null ? "false" : "true",
                ["status"] = loadResult.Context?.Cart?.Status ?? "missing"
            });
    }

    if (!loadResult.Succeeded)
    {
        trace.RecordInstantStage(
            "cart_mutated",
            outcome: "not_applied",
            metadata: new Dictionary<string, string?>
            {
                ["userId"] = userId
            });
        trace.RecordInstantStage(
            "cart_persisted",
            outcome: "not_required",
            metadata: new Dictionary<string, string?>
            {
                ["persisted"] = "false"
            });

        return CreateFailureResult(trace, loadResult.Failure!, userId, productId: null);
    }

    CartSnapshot snapshot = cartStateService.CreateSnapshot(loadResult.Context!);

    trace.RecordInstantStage(
        "cart_mutated",
        outcome: "read_only",
        metadata: new Dictionary<string, string?>
        {
            ["userId"] = userId,
            ["distinctItemCount"] = snapshot.DistinctItemCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["totalQuantity"] = snapshot.TotalQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });

    trace.RecordInstantStage(
        "cart_persisted",
        outcome: "not_required",
        metadata: new Dictionary<string, string?>
        {
            ["persisted"] = "false"
        });

    trace.MarkContractSatisfied();
    trace.AddNote(snapshot.Exists
        ? "Cart.Api returned the durable active cart state."
        : "Cart.Api returned the explicit empty-cart state because no active cart exists.");
    trace.RecordInstantStage(
        "response_sent",
        outcome: "success",
        metadata: new Dictionary<string, string?>
        {
            ["statusCode"] = StatusCodes.Status200OK.ToString(),
            ["userId"] = userId,
            ["itemCount"] = snapshot.DistinctItemCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["totalQuantity"] = snapshot.TotalQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });

    return Results.Ok(CreateCartResponse(snapshot, loadResult.LoadOutcome, "read_only", persisted: false, trace));
})
    .WithOperationContract(BusinessOperationContracts.CartGet);

app.Run();

static async Task<IResult> HandleAddAsync(
    CartItemMutationRequest request,
    RequestTraceContext trace,
    CartStateService cartStateService,
    CancellationToken cancellationToken)
{
    trace.SetUserId(request.UserId);
    trace.RecordInstantStage(
        "request_received",
        metadata: new Dictionary<string, string?>
        {
            ["userId"] = request.UserId,
            ["productId"] = request.ProductId,
            ["quantity"] = request.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });

    IResult? validationFailure = ValidateMutationRequest(trace, request);

    if (validationFailure is not null)
    {
        return validationFailure;
    }

    CartLoadResult loadResult;

    using (RequestTraceContext.StageTraceScope stage = trace.BeginStage(
        "cart_loaded_or_created",
        new Dictionary<string, string?>
        {
            ["userId"] = request.UserId,
            ["productId"] = request.ProductId
        }))
    {
        loadResult = await cartStateService.LoadCartForAddAsync(request.UserId, request.ProductId, cancellationToken);
        stage.Complete(
            loadResult.Succeeded ? loadResult.LoadOutcome : "failed",
            new Dictionary<string, string?>
            {
                ["exists"] = loadResult.Context?.Cart is null ? "false" : "true",
                ["status"] = loadResult.Context?.Cart?.Status ?? "missing"
            });
    }

    if (!loadResult.Succeeded)
    {
        trace.RecordInstantStage(
            "cart_mutated",
            outcome: "not_applied",
            metadata: new Dictionary<string, string?>
            {
                ["userId"] = request.UserId,
                ["productId"] = request.ProductId
            });
        trace.RecordInstantStage(
            "cart_persisted",
            outcome: "not_required",
            metadata: new Dictionary<string, string?>
            {
                ["persisted"] = "false"
            });

        return CreateFailureResult(trace, loadResult.Failure!, request.UserId, request.ProductId);
    }

    string mutationOutcome;

    using (RequestTraceContext.StageTraceScope stage = trace.BeginStage(
        "cart_mutated",
        new Dictionary<string, string?>
        {
            ["userId"] = request.UserId,
            ["productId"] = request.ProductId,
            ["quantity"] = request.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }))
    {
        mutationOutcome = cartStateService.ApplyAdd(loadResult.Context!, request.Quantity);
        CartSnapshot snapshot = cartStateService.CreateSnapshot(loadResult.Context!);

        stage.Complete(
            mutationOutcome,
            new Dictionary<string, string?>
            {
                ["distinctItemCount"] = snapshot.DistinctItemCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["totalQuantity"] = snapshot.TotalQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["totalPriceCents"] = snapshot.TotalPriceCents.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
    }

    bool persisted = cartStateService.RequiresPersistence(loadResult.Context!, mutationOutcome);

    if (persisted)
    {
        using RequestTraceContext.StageTraceScope stage = trace.BeginStage(
            "cart_persisted",
            new Dictionary<string, string?>
            {
                ["userId"] = request.UserId,
                ["productId"] = request.ProductId
            });

        await cartStateService.PersistAsync(cancellationToken);
        stage.Complete(
            "success",
            new Dictionary<string, string?>
            {
                ["persisted"] = "true"
            });
    }
    else
    {
        trace.RecordInstantStage(
            "cart_persisted",
            outcome: "not_required",
            metadata: new Dictionary<string, string?>
            {
                ["persisted"] = "false"
            });
    }

    CartSnapshot finalSnapshot = cartStateService.CreateSnapshot(loadResult.Context!);

    trace.MarkContractSatisfied();
    trace.AddNote("Cart.Api persisted the resulting cart state to primary.db.");
    trace.RecordInstantStage(
        "response_sent",
        outcome: "success",
        metadata: new Dictionary<string, string?>
        {
            ["statusCode"] = StatusCodes.Status200OK.ToString(),
            ["userId"] = request.UserId,
            ["productId"] = request.ProductId,
            ["itemCount"] = finalSnapshot.DistinctItemCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["totalQuantity"] = finalSnapshot.TotalQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["mutationOutcome"] = mutationOutcome
        });

    return Results.Ok(CreateCartResponse(finalSnapshot, loadResult.LoadOutcome, mutationOutcome, persisted, trace));
}

static async Task<IResult> HandleRemoveAsync(
    CartItemMutationRequest request,
    RequestTraceContext trace,
    CartStateService cartStateService,
    CancellationToken cancellationToken)
{
    trace.SetUserId(request.UserId);
    trace.RecordInstantStage(
        "request_received",
        metadata: new Dictionary<string, string?>
        {
            ["userId"] = request.UserId,
            ["productId"] = request.ProductId,
            ["quantity"] = request.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });

    IResult? validationFailure = ValidateMutationRequest(trace, request);

    if (validationFailure is not null)
    {
        return validationFailure;
    }

    CartLoadResult loadResult;

    using (RequestTraceContext.StageTraceScope stage = trace.BeginStage(
        "cart_loaded_or_created",
        new Dictionary<string, string?>
        {
            ["userId"] = request.UserId,
            ["productId"] = request.ProductId
        }))
    {
        loadResult = await cartStateService.LoadCartForRemoveAsync(request.UserId, cancellationToken);
        stage.Complete(
            loadResult.Succeeded ? loadResult.LoadOutcome : "failed",
            new Dictionary<string, string?>
            {
                ["exists"] = loadResult.Context?.Cart is null ? "false" : "true",
                ["status"] = loadResult.Context?.Cart?.Status ?? "missing"
            });
    }

    if (!loadResult.Succeeded)
    {
        trace.RecordInstantStage(
            "cart_mutated",
            outcome: "not_applied",
            metadata: new Dictionary<string, string?>
            {
                ["userId"] = request.UserId,
                ["productId"] = request.ProductId
            });
        trace.RecordInstantStage(
            "cart_persisted",
            outcome: "not_required",
            metadata: new Dictionary<string, string?>
            {
                ["persisted"] = "false"
            });

        return CreateFailureResult(trace, loadResult.Failure!, request.UserId, request.ProductId);
    }

    string mutationOutcome;

    using (RequestTraceContext.StageTraceScope stage = trace.BeginStage(
        "cart_mutated",
        new Dictionary<string, string?>
        {
            ["userId"] = request.UserId,
            ["productId"] = request.ProductId,
            ["quantity"] = request.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }))
    {
        mutationOutcome = cartStateService.ApplyRemove(loadResult.Context!, request.ProductId, request.Quantity);
        CartSnapshot snapshot = cartStateService.CreateSnapshot(loadResult.Context!);

        stage.Complete(
            mutationOutcome,
            new Dictionary<string, string?>
            {
                ["distinctItemCount"] = snapshot.DistinctItemCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["totalQuantity"] = snapshot.TotalQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["totalPriceCents"] = snapshot.TotalPriceCents.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
    }

    bool persisted = cartStateService.RequiresPersistence(loadResult.Context!, mutationOutcome);

    if (persisted)
    {
        using RequestTraceContext.StageTraceScope stage = trace.BeginStage(
            "cart_persisted",
            new Dictionary<string, string?>
            {
                ["userId"] = request.UserId,
                ["productId"] = request.ProductId
            });

        await cartStateService.PersistAsync(cancellationToken);
        stage.Complete(
            "success",
            new Dictionary<string, string?>
            {
                ["persisted"] = "true"
            });
    }
    else
    {
        trace.RecordInstantStage(
            "cart_persisted",
            outcome: "not_required",
            metadata: new Dictionary<string, string?>
            {
                ["persisted"] = "false"
            });
    }

    CartSnapshot finalSnapshot = cartStateService.CreateSnapshot(loadResult.Context!);

    trace.MarkContractSatisfied();
    trace.AddNote(persisted
        ? "Cart.Api persisted the resulting cart state to primary.db."
        : "Cart.Api returned the cart state without writing because the removal was a no-op.");
    trace.RecordInstantStage(
        "response_sent",
        outcome: "success",
        metadata: new Dictionary<string, string?>
        {
            ["statusCode"] = StatusCodes.Status200OK.ToString(),
            ["userId"] = request.UserId,
            ["productId"] = request.ProductId,
            ["itemCount"] = finalSnapshot.DistinctItemCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["totalQuantity"] = finalSnapshot.TotalQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["mutationOutcome"] = mutationOutcome
        });

    return Results.Ok(CreateCartResponse(finalSnapshot, loadResult.LoadOutcome, mutationOutcome, persisted, trace));
}

static IResult? ValidateMutationRequest(RequestTraceContext trace, CartItemMutationRequest request)
{
    if (string.IsNullOrWhiteSpace(request.UserId))
    {
        return CreateValidationFailure(
            trace,
            "invalid_user_id",
            "A non-empty user identifier is required.",
            request.UserId,
            request.ProductId);
    }

    if (string.IsNullOrWhiteSpace(request.ProductId))
    {
        return CreateValidationFailure(
            trace,
            "invalid_product_id",
            "A non-empty product identifier is required.",
            request.UserId,
            request.ProductId);
    }

    if (request.Quantity <= 0)
    {
        return CreateValidationFailure(
            trace,
            "invalid_quantity",
            "Quantity must be greater than zero for cart mutation.",
            request.UserId,
            request.ProductId);
    }

    return null;
}

static IResult CreateValidationFailure(
    RequestTraceContext trace,
    string errorCode,
    string detail,
    string? userId,
    string? productId)
{
    trace.MarkContractSatisfied();
    trace.SetErrorCode(errorCode);
    trace.RecordInstantStage(
        "cart_loaded_or_created",
        outcome: "validation_failed",
        metadata: new Dictionary<string, string?>
        {
            ["userId"] = userId,
            ["productId"] = productId
        });
    trace.RecordInstantStage(
        "cart_mutated",
        outcome: "not_applied",
        metadata: new Dictionary<string, string?>
        {
            ["userId"] = userId,
            ["productId"] = productId
        });
    trace.RecordInstantStage(
        "cart_persisted",
        outcome: "not_required",
        metadata: new Dictionary<string, string?>
        {
            ["persisted"] = "false"
        });
    trace.RecordInstantStage(
        "response_sent",
        outcome: "validation_failed",
        metadata: new Dictionary<string, string?>
        {
            ["statusCode"] = StatusCodes.Status400BadRequest.ToString(),
            ["error"] = errorCode,
            ["userId"] = userId,
            ["productId"] = productId
        });

    return Results.BadRequest(new CartErrorResponse(
        errorCode,
        detail,
        userId,
        productId,
        CreateRequestInfo(trace)));
}

static IResult CreateFailureResult(
    RequestTraceContext trace,
    CartOperationFailure failure,
    string? userId,
    string? productId)
{
    trace.MarkContractSatisfied();
    trace.SetErrorCode(failure.Code);
    trace.RecordInstantStage(
        "response_sent",
        outcome: failure.StatusCode == StatusCodes.Status404NotFound ? "not_found" : "validation_failed",
        metadata: new Dictionary<string, string?>
        {
            ["statusCode"] = failure.StatusCode.ToString(),
            ["error"] = failure.Code,
            ["userId"] = userId,
            ["productId"] = productId
        });

    CartErrorResponse body = new(
        failure.Code,
        failure.Detail,
        userId,
        productId,
        CreateRequestInfo(trace));

    return failure.StatusCode switch
    {
        StatusCodes.Status404NotFound => Results.NotFound(body),
        _ => Results.BadRequest(body)
    };
}

static CartResponse CreateCartResponse(
    CartSnapshot snapshot,
    string loadOutcome,
    string mutationOutcome,
    bool persisted,
    RequestTraceContext trace) =>
    new(
        snapshot.CartId,
        snapshot.UserId,
        snapshot.Region,
        snapshot.Exists,
        snapshot.Status,
        loadOutcome,
        mutationOutcome,
        persisted,
        snapshot.DistinctItemCount,
        snapshot.TotalQuantity,
        snapshot.TotalPriceCents,
        snapshot.Items
            .Select(item => new CartItemResponse(
                item.ProductId,
                item.Quantity,
                item.UnitPriceSnapshotCents,
                item.LineSubtotalCents,
                item.AddedUtc))
            .ToArray(),
        CreateRequestInfo(trace));

static CartRequestInfo CreateRequestInfo(RequestTraceContext trace) =>
    new(
        trace.RunId,
        trace.TraceId,
        trace.RequestId,
        trace.CorrelationId);

static RequestTraceContext GetRequiredTraceContext(IRequestTraceContextAccessor accessor) =>
    accessor.Current ?? throw new InvalidOperationException("Request trace context is not available for the current request.");

static async Task EnsurePrimaryDatabaseReadyAsync(IServiceProvider services)
{
    using IServiceScope scope = services.CreateScope();
    EnvironmentLayout layout = scope.ServiceProvider.GetRequiredService<EnvironmentLayout>();
    PrimaryDatabaseInitializer initializer = scope.ServiceProvider.GetRequiredService<PrimaryDatabaseInitializer>();
    await initializer.InitializeAsync(layout.PrimaryDatabasePath);
}
