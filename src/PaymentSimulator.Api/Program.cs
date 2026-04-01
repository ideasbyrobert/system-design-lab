using Lab.Shared.Configuration;
using Lab.Shared.Contracts;
using Lab.Shared.Http;
using Lab.Shared.Logging;
using Lab.Telemetry.AspNetCore;
using Lab.Telemetry.DependencyInjection;
using Lab.Telemetry.RequestTracing;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PaymentSimulator.Api;
using PaymentSimulator.Api.Simulation;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLabConfiguration(builder.Configuration, builder.Environment);
builder.Services.AddLabTelemetry();
builder.Services.AddRequestTraceJsonlWriter();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<InMemoryPaymentSimulationStore>();
builder.Services.AddHttpClient("payment-callback-dispatcher");
builder.Services.AddHostedService<PaymentCallbackDispatcher>();
builder.Logging.AddLabOperationalFileLogging();

var app = builder.Build();
app.LogResolvedLabEnvironment();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        ILogger logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("PaymentSimulator.Exceptions");
        IProblemDetailsService problemDetails = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        IExceptionHandlerFeature? exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();

        logger.LogError(exceptionFeature?.Error, "Unhandled exception reached the PaymentSimulator exception boundary.");

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Unhandled PaymentSimulator error",
                Detail = "The PaymentSimulator host hit an unhandled exception while processing the request.",
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
        "describe_payment_simulator_host",
        metadata: new Dictionary<string, string?>
        {
            ["service"] = layout.ServiceName,
            ["region"] = layout.CurrentRegion
        });

    trace.MarkContractSatisfied();
    trace.AddNote("PaymentSimulator host-info endpoint completed successfully.");

    return Results.Ok(new
    {
        layout.ServiceName,
        layout.CurrentRegion,
        layout.RepositoryRoot,
        Request = CreateRequestInfo(trace)
    });
})
    .WithOperationContract(BusinessOperationContracts.PaymentSimulatorHostInfo);

app.MapPost("/payments/authorize", async (
    [FromBody] PaymentAuthorizationRequest request,
    HttpContext httpContext,
    IOptions<PaymentSimulatorOptions> simulatorOptions,
    InMemoryPaymentSimulationStore store,
    TimeProvider timeProvider,
    IRequestTraceContextAccessor traceAccessor,
    CancellationToken cancellationToken) =>
{
    RequestTraceContext trace = GetRequiredTraceContext(traceAccessor);
    PaymentModeResolution modeResolution = PaymentSimulationModeResolver.Resolve(httpContext.Request, simulatorOptions.Value);

    trace.RecordInstantStage(
        "request_received",
        metadata: new Dictionary<string, string?>
        {
            ["paymentId"] = request.PaymentId,
            ["orderId"] = request.OrderId,
            ["amountCents"] = request.AmountCents.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["mode"] = modeResolution.IsValid ? PaymentSimulationModeResolver.ToExternalText(modeResolution.Mode) : modeResolution.RawValue,
            ["modeSource"] = modeResolution.Source,
            ["callbackUrl"] = request.CallbackUrl
        });

    if (!modeResolution.IsValid)
    {
        trace.SetErrorCode("invalid_payment_mode");
        trace.RecordInstantStage(
            "response_sent",
            outcome: "validation_failed",
            metadata: new Dictionary<string, string?>
            {
                ["statusCode"] = StatusCodes.Status400BadRequest.ToString(),
                ["error"] = "invalid_payment_mode",
                ["modeSource"] = modeResolution.Source
            });

        return Results.BadRequest(CreateFailureResponse(
            trace,
            request,
            mode: modeResolution.RawValue ?? string.Empty,
            modeSource: modeResolution.Source,
            providerReference: null,
            attemptNumber: 0,
            error: "invalid_payment_mode",
            detail: $"The requested payment simulation mode '{modeResolution.RawValue}' is not supported."));
    }

    IResult? validationFailure = ValidateAuthorizationRequest(trace, request, modeResolution);

    if (validationFailure is not null)
    {
        return validationFailure;
    }

    string modeText = PaymentSimulationModeResolver.ToExternalText(modeResolution.Mode);
    httpContext.Response.Headers[LabHeaderNames.PaymentSimulatorMode] = modeText;

    PaymentSimulationAttemptState attempt = store.BeginAttempt(request, modeResolution.Mode);
    string providerReference = CreateProviderReference(request.PaymentId, attempt.AttemptNumber);

    trace.RecordInstantStage(
        "mode_resolved",
        metadata: new Dictionary<string, string?>
        {
            ["mode"] = modeText,
            ["modeSource"] = modeResolution.Source,
            ["attemptNumber"] = attempt.AttemptNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["providerReference"] = providerReference
        });

    string outcome;
    int statusCode;
    bool callbackPending;
    int callbackCountScheduled;
    string? errorCode = null;
    string? errorDetail = null;

    using (RequestTraceContext.StageTraceScope stage = trace.BeginStage(
        "authorization_simulated",
        new Dictionary<string, string?>
        {
            ["mode"] = modeText,
            ["attemptNumber"] = attempt.AttemptNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["providerReference"] = providerReference
        }))
    {
        (outcome, statusCode, callbackPending, callbackCountScheduled, errorCode, errorDetail) =
            await SimulateAuthorizationAsync(
                request,
                attempt,
                providerReference,
                modeResolution.Mode,
                simulatorOptions.Value,
                store,
                timeProvider,
                cancellationToken);

        stage.Complete(
            statusCode >= 500 ? "simulated_failure" : "simulated_success",
            new Dictionary<string, string?>
            {
                ["statusCode"] = statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["outcome"] = outcome,
                ["callbackCountScheduled"] = callbackCountScheduled.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
    }

    store.CompleteAttempt(
        request.PaymentId,
        modeResolution.Mode,
        outcome,
        request.AmountCents,
        request.Currency,
        providerReference);

    if (callbackCountScheduled > 0)
    {
        trace.RecordInstantStage(
            "callback_scheduled",
            metadata: new Dictionary<string, string?>
            {
                ["paymentId"] = request.PaymentId,
                ["providerReference"] = providerReference,
                ["callbackCount"] = callbackCountScheduled.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["callbackUrl"] = attempt.CallbackUrl
            });
    }

    trace.MarkContractSatisfied();

    if (errorCode is not null)
    {
        trace.SetErrorCode(errorCode);
    }

    trace.RecordInstantStage(
        "response_sent",
        outcome: statusCode >= 500 ? "simulated_failure" : statusCode == StatusCodes.Status202Accepted ? "pending_confirmation" : "success",
        metadata: new Dictionary<string, string?>
        {
            ["statusCode"] = statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["providerReference"] = providerReference,
            ["mode"] = modeText,
            ["attemptNumber"] = attempt.AttemptNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["callbackCountScheduled"] = callbackCountScheduled.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });

    if (errorCode is not null)
    {
        return Results.Json(
            CreateFailureResponse(
                trace,
                request,
                modeText,
                modeResolution.Source,
                providerReference,
                attempt.AttemptNumber,
                errorCode,
                errorDetail ?? errorCode),
            statusCode: statusCode);
    }

    return Results.Json(
        CreateSuccessResponse(
            trace,
            request,
            modeText,
            modeResolution.Source,
            providerReference,
            attempt.AttemptNumber,
            outcome,
            callbackPending,
            callbackCountScheduled),
        statusCode: statusCode);
})
    .WithOperationContract(BusinessOperationContracts.PaymentAuthorize);

app.MapGet("/payments/authorizations/{paymentId}", (
    string paymentId,
    InMemoryPaymentSimulationStore store,
    IRequestTraceContextAccessor traceAccessor) =>
{
    RequestTraceContext trace = GetRequiredTraceContext(traceAccessor);
    trace.RecordInstantStage(
        "request_received",
        metadata: new Dictionary<string, string?>
        {
            ["paymentId"] = paymentId
        });

    if (string.IsNullOrWhiteSpace(paymentId))
    {
        trace.SetErrorCode("invalid_payment_id");
        trace.RecordInstantStage(
            "response_sent",
            outcome: "validation_failed",
            metadata: new Dictionary<string, string?>
            {
                ["statusCode"] = StatusCodes.Status400BadRequest.ToString(),
                ["error"] = "invalid_payment_id"
            });

        return Results.BadRequest(new
        {
            error = "invalid_payment_id",
            detail = "A non-empty payment identifier is required.",
            paymentId,
            Request = CreateRequestInfo(trace)
        });
    }

    PaymentSimulationStatusSnapshot? snapshot = store.GetStatus(paymentId);

    if (snapshot is null)
    {
        trace.MarkContractSatisfied();
        trace.RecordInstantStage(
            "response_sent",
            outcome: "not_found",
            metadata: new Dictionary<string, string?>
            {
                ["statusCode"] = StatusCodes.Status404NotFound.ToString(),
                ["paymentId"] = paymentId
            });

        return Results.NotFound(new
        {
            error = "payment_not_found",
            detail = $"No simulator-side state exists for payment '{paymentId}'.",
            paymentId,
            Request = CreateRequestInfo(trace)
        });
    }

    trace.MarkContractSatisfied();
    trace.RecordInstantStage(
        "response_sent",
        outcome: "success",
        metadata: new Dictionary<string, string?>
        {
            ["statusCode"] = StatusCodes.Status200OK.ToString(),
            ["paymentId"] = paymentId,
            ["attemptCount"] = snapshot.AttemptCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });

    return Results.Ok(CreateStatusResponse(trace, snapshot));
})
    .WithOperationContract(BusinessOperationContracts.PaymentAuthorizationStatus);

app.Run();

static PaymentAuthorizationResponse CreateSuccessResponse(
    RequestTraceContext trace,
    PaymentAuthorizationRequest request,
    string mode,
    string modeSource,
    string providerReference,
    int attemptNumber,
    string outcome,
    bool callbackPending,
    int callbackCountScheduled) =>
    new(
        PaymentId: request.PaymentId,
        OrderId: request.OrderId,
        ProviderReference: providerReference,
        Mode: mode,
        Outcome: outcome,
        ModeSource: modeSource,
        AttemptNumber: attemptNumber,
        AmountCents: request.AmountCents,
        Currency: request.Currency,
        CallbackPending: callbackPending,
        CallbackCountScheduled: callbackCountScheduled,
        Request: CreateRequestInfo(trace));

static PaymentAuthorizationFailureResponse CreateFailureResponse(
    RequestTraceContext trace,
    PaymentAuthorizationRequest request,
    string mode,
    string modeSource,
    string? providerReference,
    int attemptNumber,
    string error,
    string detail) =>
    new(
        Error: error,
        Detail: detail,
        PaymentId: request.PaymentId,
        OrderId: request.OrderId,
        Mode: mode,
        ModeSource: modeSource,
        ProviderReference: providerReference,
        AttemptNumber: attemptNumber,
        AmountCents: request.AmountCents,
        Currency: request.Currency,
        Request: CreateRequestInfo(trace));

static PaymentAuthorizationStatusResponse CreateStatusResponse(
    RequestTraceContext trace,
    PaymentSimulationStatusSnapshot snapshot) =>
    new(
        PaymentId: snapshot.PaymentId,
        OrderId: snapshot.OrderId,
        Mode: snapshot.Mode,
        Outcome: snapshot.Outcome,
        AttemptCount: snapshot.AttemptCount,
        LatestProviderReference: snapshot.LatestProviderReference,
        AmountCents: snapshot.AmountCents,
        Currency: snapshot.Currency,
        CallbackUrl: snapshot.CallbackUrl,
        Callbacks: snapshot.Callbacks
            .Select(callback => new PaymentCallbackStatusResponse(
                callback.CallbackId,
                callback.SequenceNumber,
                callback.Status,
                callback.DueUtc,
                callback.CompletedUtc,
                callback.DeliveryAttempts,
                callback.LastError))
            .ToArray(),
        Request: CreateRequestInfo(trace));

static PaymentSimulatorRequestInfo CreateRequestInfo(RequestTraceContext trace) =>
    new(
        RunId: trace.RunId,
        TraceId: trace.TraceId,
        RequestId: trace.RequestId,
        CorrelationId: trace.CorrelationId);

static IResult? ValidateAuthorizationRequest(
    RequestTraceContext trace,
    PaymentAuthorizationRequest request,
    PaymentModeResolution modeResolution)
{
    if (string.IsNullOrWhiteSpace(request.PaymentId))
    {
        return ValidationFailure(trace, request, modeResolution, "invalid_payment_id", "A non-empty payment identifier is required.");
    }

    if (string.IsNullOrWhiteSpace(request.OrderId))
    {
        return ValidationFailure(trace, request, modeResolution, "invalid_order_id", "A non-empty order identifier is required.");
    }

    if (request.AmountCents <= 0)
    {
        return ValidationFailure(trace, request, modeResolution, "invalid_amount", "Payment amount must be greater than zero.");
    }

    if (string.IsNullOrWhiteSpace(request.Currency))
    {
        return ValidationFailure(trace, request, modeResolution, "invalid_currency", "A non-empty currency is required.");
    }

    return null;
}

static IResult ValidationFailure(
    RequestTraceContext trace,
    PaymentAuthorizationRequest request,
    PaymentModeResolution modeResolution,
    string error,
    string detail)
{
    trace.SetErrorCode(error);
    trace.RecordInstantStage(
        "response_sent",
        outcome: "validation_failed",
        metadata: new Dictionary<string, string?>
        {
            ["statusCode"] = StatusCodes.Status400BadRequest.ToString(),
            ["error"] = error,
            ["modeSource"] = modeResolution.Source
        });

    return Results.BadRequest(CreateFailureResponse(
        trace,
        request,
        modeResolution.IsValid ? PaymentSimulationModeResolver.ToExternalText(modeResolution.Mode) : modeResolution.RawValue ?? string.Empty,
        modeResolution.Source,
        providerReference: null,
        attemptNumber: 0,
        error: error,
        detail: detail));
}

static async Task<(string Outcome, int StatusCode, bool CallbackPending, int CallbackCountScheduled, string? ErrorCode, string? ErrorDetail)> SimulateAuthorizationAsync(
    PaymentAuthorizationRequest request,
    PaymentSimulationAttemptState attempt,
    string providerReference,
    PaymentSimulationMode mode,
    PaymentSimulatorOptions options,
    InMemoryPaymentSimulationStore store,
    TimeProvider timeProvider,
    CancellationToken cancellationToken)
{
    switch (mode)
    {
        case PaymentSimulationMode.FastSuccess:
            await Task.Delay(Math.Max(0, options.FastLatencyMilliseconds), cancellationToken);
            return ("authorized", StatusCodes.Status200OK, false, 0, null, null);

        case PaymentSimulationMode.SlowSuccess:
            await Task.Delay(Math.Max(0, options.SlowLatencyMilliseconds), cancellationToken);
            return ("authorized", StatusCodes.Status200OK, false, 0, null, null);

        case PaymentSimulationMode.Timeout:
            await Task.Delay(Math.Max(0, options.TimeoutLatencyMilliseconds), cancellationToken);
            return ("timeout", StatusCodes.Status504GatewayTimeout, false, 0, "simulated_timeout", "The payment provider intentionally timed out.");

        case PaymentSimulationMode.TransientFailure:
            await Task.Delay(Math.Max(0, options.FastLatencyMilliseconds), cancellationToken);

            if (attempt.AttemptNumber == 1)
            {
                return (
                    "transient_failure",
                    StatusCodes.Status503ServiceUnavailable,
                    false,
                    0,
                    "simulated_transient_failure",
                    "The payment provider intentionally failed the first attempt.");
            }

            return ("authorized", StatusCodes.Status200OK, false, 0, null, null);

        case PaymentSimulationMode.DelayedConfirmation:
        {
            await Task.Delay(Math.Max(0, options.FastLatencyMilliseconds), cancellationToken);
            DateTimeOffset dueUtc = timeProvider.GetUtcNow().AddMilliseconds(Math.Max(0, options.DelayedConfirmationMilliseconds));
            store.ScheduleCallbacks(
                request.PaymentId,
                [
                    new ScheduledPaymentCallback(
                        callbackId: $"cb-{Guid.NewGuid():N}",
                        paymentId: request.PaymentId,
                        orderId: request.OrderId,
                        providerReference: providerReference,
                        mode: PaymentSimulationModeResolver.ToExternalText(mode),
                        amountCents: request.AmountCents,
                        currency: request.Currency,
                        callbackUrl: attempt.CallbackUrl,
                        sequenceNumber: 1,
                        dueUtc: dueUtc)
                ]);

            return ("pending_confirmation", StatusCodes.Status202Accepted, true, 1, null, null);
        }

        case PaymentSimulationMode.DuplicateCallback:
        {
            await Task.Delay(Math.Max(0, options.FastLatencyMilliseconds), cancellationToken);
            DateTimeOffset dueUtc = timeProvider.GetUtcNow().AddMilliseconds(Math.Max(0, options.DelayedConfirmationMilliseconds));
            store.ScheduleCallbacks(
                request.PaymentId,
                [
                    new ScheduledPaymentCallback(
                        callbackId: $"cb-{Guid.NewGuid():N}",
                        paymentId: request.PaymentId,
                        orderId: request.OrderId,
                        providerReference: providerReference,
                        mode: PaymentSimulationModeResolver.ToExternalText(mode),
                        amountCents: request.AmountCents,
                        currency: request.Currency,
                        callbackUrl: attempt.CallbackUrl,
                        sequenceNumber: 1,
                        dueUtc: dueUtc),
                    new ScheduledPaymentCallback(
                        callbackId: $"cb-{Guid.NewGuid():N}",
                        paymentId: request.PaymentId,
                        orderId: request.OrderId,
                        providerReference: providerReference,
                        mode: PaymentSimulationModeResolver.ToExternalText(mode),
                        amountCents: request.AmountCents,
                        currency: request.Currency,
                        callbackUrl: attempt.CallbackUrl,
                        sequenceNumber: 2,
                        dueUtc: dueUtc.AddMilliseconds(Math.Max(0, options.DuplicateCallbackSpacingMilliseconds)))
                ]);

            return ("pending_confirmation", StatusCodes.Status202Accepted, true, 2, null, null);
        }

        default:
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported payment simulation mode.");
    }
}

static string CreateProviderReference(string paymentId, int attemptNumber)
{
    string normalizedPaymentId = string.IsNullOrWhiteSpace(paymentId) ? "unknown" : paymentId.Trim();
    return $"psim-{normalizedPaymentId}-{attemptNumber:D4}";
}

static RequestTraceContext GetRequiredTraceContext(IRequestTraceContextAccessor accessor) =>
    accessor.Current ?? throw new InvalidOperationException("Request trace context is not available for the current request.");

public partial class Program
{
}
