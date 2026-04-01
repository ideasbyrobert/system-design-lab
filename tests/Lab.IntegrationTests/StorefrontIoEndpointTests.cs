using System.Text.Json;
using Lab.Shared.Http;
using Lab.Telemetry.RequestTracing;

namespace Lab.IntegrationTests;

[TestClass]
public sealed class StorefrontIoEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task IoEndpoint_TracksConfiguredDelay_And_EmitsWaitStages()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        await using StorefrontFactory factory = new(repositoryRoot);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage faster = await client.GetAsync("/io?delayMs=10&jitterMs=0");
        HttpResponseMessage slower = await client.GetAsync("/io?delayMs=40&jitterMs=0");

        Assert.AreEqual(System.Net.HttpStatusCode.OK, faster.StatusCode);
        Assert.AreEqual(System.Net.HttpStatusCode.OK, slower.StatusCode);

        JsonElement fasterJson = JsonSerializer.Deserialize<JsonElement>(await faster.Content.ReadAsStringAsync(), JsonOptions);
        JsonElement slowerJson = JsonSerializer.Deserialize<JsonElement>(await slower.Content.ReadAsStringAsync(), JsonOptions);
        string fasterTraceId = fasterJson.GetProperty("request").GetProperty("traceId").GetString()!;
        string slowerTraceId = slowerJson.GetProperty("request").GetProperty("traceId").GetString()!;

        Assert.AreEqual(10, fasterJson.GetProperty("appliedDelayMs").GetInt32());
        Assert.AreEqual(40, slowerJson.GetProperty("appliedDelayMs").GetInt32());

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        Assert.HasCount(2, traces);

        RequestTraceTestHelper.TraceEnvelope fasterTrace = traces.Single(trace => trace.Record.TraceId == fasterTraceId);
        RequestTraceTestHelper.TraceEnvelope slowerTrace = traces.Single(trace => trace.Record.TraceId == slowerTraceId);

        RequestTraceTestHelper.AssertRequiredFieldsPresent(fasterTrace.Json);
        RequestTraceTestHelper.AssertRequiredFieldsPresent(slowerTrace.Json);
        RequestTraceTestHelper.AssertMilestone1Trace(fasterTrace.Record, "io-bound-lab", "/io", "GET", 200, expectedContractSatisfied: true);
        RequestTraceTestHelper.AssertMilestone1Trace(slowerTrace.Record, "io-bound-lab", "/io", "GET", 200, expectedContractSatisfied: true);

        CollectionAssert.AreEquivalent(
            new[]
            {
                "request_received",
                "downstream_wait_started",
                "downstream_wait",
                "downstream_wait_completed",
                "response_sent",
                "http_request"
            },
            fasterTrace.Record.StageTimings.Select(stage => stage.StageName).ToArray());

        StageTimingRecord fasterWait = fasterTrace.Record.StageTimings.Single(stage => stage.StageName == "downstream_wait");
        StageTimingRecord slowerWait = slowerTrace.Record.StageTimings.Single(stage => stage.StageName == "downstream_wait");

        Assert.AreEqual("10", fasterWait.Metadata["appliedDelayMs"]);
        Assert.AreEqual("40", slowerWait.Metadata["appliedDelayMs"]);
        Assert.IsGreaterThanOrEqualTo(8d, fasterWait.ElapsedMs, $"Expected wait >= 8ms, actual {fasterWait.ElapsedMs:0.###}ms.");
        Assert.IsGreaterThanOrEqualTo(35d, slowerWait.ElapsedMs, $"Expected wait >= 35ms, actual {slowerWait.ElapsedMs:0.###}ms.");
    }

    [TestMethod]
    public async Task IoEndpoint_ValidationFailure_PersistsHeadersRegionAndRequiredFields()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        await using StorefrontFactory factory = new(repositoryRoot);
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, "/io?delayMs=-1&jitterMs=0");

        request.Headers.TryAddWithoutValidation(LabHeaderNames.RunId, "io-run-013");
        request.Headers.TryAddWithoutValidation(LabHeaderNames.CorrelationId, "io-corr-013");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, response.StatusCode);

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        Assert.HasCount(1, traces);

        RequestTraceTestHelper.TraceEnvelope trace = traces[0];
        RequestTraceTestHelper.AssertRequiredFieldsPresent(trace.Json);
        RequestTraceTestHelper.AssertMilestone1Trace(trace.Record, "io-bound-lab", "/io", "GET", 400, expectedContractSatisfied: false);

        Assert.AreEqual("io-run-013", trace.Record.RunId);
        Assert.AreEqual("io-corr-013", trace.Record.CorrelationId);
        Assert.AreEqual("local", trace.Record.Region);
        Assert.AreEqual("error", trace.Record.StageTimings.Single(stage => stage.StageName == "http_request").Outcome);
        Assert.IsTrue(response.Headers.Contains(LabHeaderNames.RunId));
        Assert.IsTrue(response.Headers.Contains(LabHeaderNames.CorrelationId));
    }

    private static string CreateUniqueTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
