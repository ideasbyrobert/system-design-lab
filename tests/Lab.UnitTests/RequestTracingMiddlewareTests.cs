using Lab.Shared.Contracts;
using Lab.Telemetry.AspNetCore;
using Lab.Telemetry.Persistence;
using Lab.Telemetry.RequestTracing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Lab.UnitTests;

[TestClass]
public sealed class RequestTracingMiddlewareTests
{
    [TestMethod]
    public async Task InvokeAsync_WhenNextThrows_PersistsA500TraceWithErrorMetadata()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => { });
        RequestTracingMiddleware middleware = new(
            _ => throw new InvalidOperationException("boom"),
            loggerFactory.CreateLogger<RequestTracingMiddleware>());
        RequestTraceFactory factory = new("Storefront.Api", "local", runId: "run-error");
        CapturingRequestTraceWriter writer = new();
        TestRequestTraceContextAccessor accessor = new();
        DefaultHttpContext context = new();

        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/cpu";
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new OperationContractMetadata(BusinessOperationContracts.CpuBoundLab)),
            "cpu"));

        try
        {
            await middleware.InvokeAsync(context, factory, writer, accessor);
            Assert.Fail("Expected InvalidOperationException from the downstream delegate.");
        }
        catch (InvalidOperationException)
        {
        }

        Assert.IsNull(accessor.Current);
        Assert.IsNotNull(writer.Record);

        RequestTraceRecord record = writer.Record;

        Assert.AreEqual("cpu-bound-lab", record.Operation);
        Assert.AreEqual("/cpu", record.Route);
        Assert.AreEqual("GET", record.Method);
        Assert.AreEqual(500, record.StatusCode);
        Assert.AreEqual("InvalidOperationException", record.ErrorCode);
        Assert.IsFalse(record.ContractSatisfied);

        StageTimingRecord stage = record.StageTimings.Single(item => item.StageName == "http_request");
        Assert.AreEqual("error", stage.Outcome);
        Assert.AreEqual("500", stage.Metadata["statusCode"]);
        Assert.AreEqual("InvalidOperationException", stage.Metadata["exception"]);
    }

    [TestMethod]
    public async Task InvokeAsync_IgnoresRequestAbortedWhenPersistingTrace()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => { });
        RequestTracingMiddleware middleware = new(
            _ => Task.CompletedTask,
            loggerFactory.CreateLogger<RequestTracingMiddleware>());
        RequestTraceFactory factory = new("Storefront.Api", "local", runId: "run-aborted");
        CapturingRequestTraceWriter writer = new();
        TestRequestTraceContextAccessor accessor = new();
        DefaultHttpContext context = new();

        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/health";
        context.Response.Body = new MemoryStream();

        CancellationTokenSource requestAbortSource = new();
        requestAbortSource.Cancel();
        context.RequestAborted = requestAbortSource.Token;

        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new OperationContractMetadata(BusinessOperationContracts.HealthCheck)),
            "health"));

        await middleware.InvokeAsync(context, factory, writer, accessor);

        Assert.IsNull(accessor.Current);
        Assert.IsNotNull(writer.Record);
        Assert.IsFalse(writer.CancellationToken.CanBeCanceled);
    }

    private sealed class TestRequestTraceContextAccessor : IRequestTraceContextAccessor
    {
        public RequestTraceContext? Current { get; set; }
    }

    private sealed class CapturingRequestTraceWriter : IRequestTraceWriter
    {
        public RequestTraceRecord Record { get; private set; } = null!;
        public CancellationToken CancellationToken { get; private set; }

        public ValueTask<bool> WriteAsync(RequestTraceRecord traceRecord, CancellationToken cancellationToken = default)
        {
            Record = traceRecord;
            CancellationToken = cancellationToken;
            return ValueTask.FromResult(true);
        }
    }
}
