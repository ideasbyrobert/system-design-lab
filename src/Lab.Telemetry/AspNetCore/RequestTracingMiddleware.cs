using Lab.Shared.Contracts;
using Lab.Shared.Http;
using Lab.Telemetry.Persistence;
using Lab.Telemetry.RequestTracing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Lab.Telemetry.AspNetCore;

public sealed class RequestTracingMiddleware(RequestDelegate next, ILogger<RequestTracingMiddleware> logger)
{
    private static readonly OperationContractDescriptor UnmappedRequestContract = OperationContractDescriptor.Create(
        operationName: "unmapped-request",
        inputs:
        [
            "route",
            "method"
        ],
        preconditions:
        [
            "The request reached Storefront.Api."
        ],
        postconditions:
        [
            "The response reports the actual HTTP outcome for the unmapped request."
        ],
        invariants:
        [
            "The observation boundary is still the top-level Storefront response."
        ],
        observationStart: "The unmapped request arrives at Storefront.Api.",
        observationEnd: "Storefront.Api sends the HTTP response for the unmapped request.");

    public async Task InvokeAsync(
        HttpContext context,
        IRequestTraceFactory requestTraceFactory,
        IRequestTraceWriter requestTraceWriter,
        IRequestTraceContextAccessor accessor)
    {
        string? incomingRequestId = NormalizeHeader(context.Request.Headers[LabHeaderNames.RequestId]);

        if (!string.IsNullOrWhiteSpace(incomingRequestId))
        {
            context.TraceIdentifier = incomingRequestId;
        }

        string? incomingRunId = NormalizeHeader(context.Request.Headers[LabHeaderNames.RunId]);
        string? incomingTraceId = NormalizeHeader(context.Request.Headers[LabHeaderNames.TraceId]);
        string correlationId = NormalizeHeader(context.Request.Headers[LabHeaderNames.CorrelationId]) ?? context.TraceIdentifier;
        string route = context.Request.Path.HasValue ? context.Request.Path.Value! : "/";

        OperationContractDescriptor contract = context.GetEndpoint()?.Metadata.GetMetadata<OperationContractMetadata>()?.Contract
            ?? UnmappedRequestContract;

        RequestTraceContext trace = requestTraceFactory.BeginRequest(
            contract: contract,
            route: route,
            method: context.Request.Method,
            requestId: context.TraceIdentifier,
            runId: incomingRunId,
            correlationId: correlationId,
            traceId: incomingTraceId);

        accessor.Current = trace;
        context.Items[typeof(RequestTraceContext)] = trace;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[LabHeaderNames.RunId] = trace.RunId;
            context.Response.Headers[LabHeaderNames.CorrelationId] = trace.CorrelationId ?? correlationId;
            context.Response.Headers[LabHeaderNames.RequestId] = trace.RequestId;
            context.Response.Headers[LabHeaderNames.TraceId] = trace.TraceId;
            return Task.CompletedTask;
        });

        RequestTraceContext.StageTraceScope stage = trace.BeginStage(
            "http_request",
            new Dictionary<string, string?>
            {
                ["path"] = route,
                ["method"] = context.Request.Method
            });

        Exception? unhandled = null;

        try
        {
            await next(context);
            stage.Complete(
                context.Response.StatusCode >= 400 ? "error" : "success",
                new Dictionary<string, string?>
                {
                    ["statusCode"] = context.Response.StatusCode.ToString()
                });
        }
        catch (Exception exception)
        {
            unhandled = exception;
            trace.SetErrorCode(exception.GetType().Name);
            stage.Complete(
                "error",
                new Dictionary<string, string?>
                {
                    ["statusCode"] = StatusCodes.Status500InternalServerError.ToString(),
                    ["exception"] = exception.GetType().Name
                });

            throw;
        }
        finally
        {
            accessor.Current = null;
            int statusCode = unhandled is null ? ResolveStatusCode(context.Response.StatusCode) : StatusCodes.Status500InternalServerError;
            RequestTraceRecord record = trace.Complete(statusCode, unhandled?.GetType().Name);
            bool persisted = false;

            try
            {
                persisted = await requestTraceWriter.WriteAsync(record, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Request trace writer threw while persisting trace {TraceId}.", record.TraceId);
            }

            if (!persisted)
            {
                logger.LogWarning(
                    "Request trace {TraceId} for route {Route} was not persisted successfully.",
                    record.TraceId,
                    record.Route);
            }
        }
    }

    private static int ResolveStatusCode(int statusCode) =>
        statusCode == 0 ? StatusCodes.Status200OK : statusCode;

    private static string? NormalizeHeader(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
