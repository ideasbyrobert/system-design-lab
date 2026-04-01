using Lab.Persistence.DependencyInjection;
using Lab.Shared.Configuration;
using Lab.Shared.Contracts;
using Lab.Shared.Http;
using Lab.Shared.Logging;
using Lab.Shared.Networking;
using Lab.Telemetry.AspNetCore;
using Lab.Telemetry.DependencyInjection;
using Lab.Telemetry.RequestTracing;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Order.Api.Checkout;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLabConfiguration(builder.Configuration, builder.Environment);
builder.Services.AddLabTelemetry();
builder.Services.AddRequestTraceJsonlWriter();
builder.Services.AddPrimaryPersistence();
builder.Services.AddScoped<OrderCheckoutService>();
builder.Services.AddHttpClient<IOrderPaymentClient, HttpOrderPaymentClient>((serviceProvider, httpClient) =>
{
    ServiceEndpointOptions options = serviceProvider.GetRequiredService<IOptions<ServiceEndpointOptions>>().Value;
    httpClient.BaseAddress = new Uri(options.PaymentSimulatorBaseUrl, UriKind.Absolute);
    httpClient.Timeout = Timeout.InfiniteTimeSpan;
})
    .AddRegionLatencyInjection(
        "payment-simulator",
        serviceProvider => serviceProvider.GetRequiredService<IOptions<ServiceEndpointOptions>>().Value.PaymentSimulatorRegion);
builder.Services.AddProblemDetails();
builder.Logging.AddLabOperationalFileLogging();

var app = builder.Build();
app.LogResolvedLabEnvironment();
await EnsurePrimaryDatabaseReadyAsync(app.Services);

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        ILogger logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Order.Exceptions");
        IProblemDetailsService problemDetails = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        IExceptionHandlerFeature? exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();

        logger.LogError(exceptionFeature?.Error, "Unhandled exception reached the Order exception boundary.");

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Unhandled Order error",
                Detail = "The Order host hit an unhandled exception while processing the request.",
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
        "describe_order_host",
        metadata: new Dictionary<string, string?>
        {
            ["service"] = layout.ServiceName,
            ["region"] = layout.CurrentRegion
        });

    trace.MarkContractSatisfied();
    trace.AddNote("Order host-info endpoint completed successfully.");

    return Results.Ok(new OrderHostInfoResponse(
        layout.ServiceName,
        layout.CurrentRegion,
        layout.RepositoryRoot,
        CreateRequestInfo(trace)));
})
    .WithOperationContract(BusinessOperationContracts.OrderHostInfo);

app.MapPost("/orders/checkout", async (
    string? mode,
    [FromBody] OrderCheckoutRequest request,
    [FromHeader(Name = LabHeaderNames.IdempotencyKey)] string? idempotencyKey,
    HttpContext httpContext,
    OrderCheckoutService checkoutService,
    IRequestTraceContextAccessor traceAccessor,
    CancellationToken cancellationToken) =>
{
    request.IdempotencyKey = idempotencyKey ?? string.Empty;
    request.DebugTelemetryRequested = IsDebugTelemetryRequested(httpContext.Request);
    RequestTraceContext trace = GetRequiredTraceContext(traceAccessor);
    OrderCheckoutExecutionResult result = await checkoutService.ExecuteCheckoutAsync(request, mode, trace, cancellationToken);

    if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
    {
        httpContext.Response.Headers[LabHeaderNames.IdempotencyKey] = request.IdempotencyKey;
    }

    if (result.ContractSatisfied)
    {
        trace.MarkContractSatisfied();
    }

    if (result.ErrorCode is not null)
    {
        trace.SetErrorCode(result.ErrorCode);
    }

    trace.RecordInstantStage(
        "response_sent",
        outcome: result.ResponseOutcome,
        metadata: new Dictionary<string, string?>
        {
            ["statusCode"] = result.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["orderId"] = result.Response?.OrderId,
            ["paymentId"] = result.Response?.PaymentId,
            ["paymentStatus"] = result.Response?.PaymentStatus,
            ["checkoutMode"] = result.Response?.CheckoutMode ?? result.Failure?.CheckoutMode ?? mode,
            ["backgroundJobId"] = result.Response?.BackgroundJobId,
            ["errorCode"] = result.ErrorCode
        });

    OrderDebugTelemetryInfo? debugTelemetry = request.DebugTelemetryRequested ? CreateDebugTelemetry(trace) : null;

    return result.Response is not null
        ? Results.Json(result.Response with { DebugTelemetry = debugTelemetry }, statusCode: result.StatusCode)
        : Results.Json(result.Failure! with { DebugTelemetry = debugTelemetry }, statusCode: result.StatusCode);
})
    .WithOperationContract(BusinessOperationContracts.OrderCheckout);

app.Run();

static async Task EnsurePrimaryDatabaseReadyAsync(IServiceProvider services)
{
    using IServiceScope scope = services.CreateScope();
    Lab.Persistence.PrimaryDatabaseInitializer initializer = scope.ServiceProvider.GetRequiredService<Lab.Persistence.PrimaryDatabaseInitializer>();
    EnvironmentLayout layout = scope.ServiceProvider.GetRequiredService<EnvironmentLayout>();
    await initializer.InitializeAsync(layout.PrimaryDatabasePath);
}

static OrderRequestInfo CreateRequestInfo(RequestTraceContext trace) =>
    new(
        RunId: trace.RunId,
        TraceId: trace.TraceId,
        RequestId: trace.RequestId,
        CorrelationId: trace.CorrelationId);

static OrderDebugTelemetryInfo CreateDebugTelemetry(RequestTraceContext trace) =>
    new(
        StageTimings: trace.StageTimings
            .Select(stage => new OrderDebugStageInfo(
                StageName: stage.StageName,
                ElapsedMs: stage.ElapsedMs,
                Outcome: stage.Outcome,
                Metadata: stage.Metadata))
            .ToArray(),
        DependencyCalls: trace.DependencyCalls
            .Select(call => new OrderDebugDependencyInfo(
                DependencyName: call.DependencyName,
                Route: call.Route,
                Region: call.Region,
                ElapsedMs: call.ElapsedMs,
                StatusCode: call.StatusCode,
                Outcome: call.Outcome,
                Metadata: call.Metadata,
                Notes: call.Notes))
            .ToArray(),
        Notes: trace.Notes.ToArray());

static RequestTraceContext GetRequiredTraceContext(IRequestTraceContextAccessor accessor) =>
    accessor.Current ?? throw new InvalidOperationException("Request trace context is not available for the current request.");

static bool IsDebugTelemetryRequested(HttpRequest request)
{
    if (!request.Headers.TryGetValue(LabHeaderNames.DebugTelemetry, out Microsoft.Extensions.Primitives.StringValues values))
    {
        return false;
    }

    return bool.TryParse(values.ToString(), out bool enabled) && enabled;
}

public partial class Program
{
}
