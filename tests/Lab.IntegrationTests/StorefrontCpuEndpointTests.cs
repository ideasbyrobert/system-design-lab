using System.Text.Json;
using Lab.Telemetry.RequestTracing;

namespace Lab.IntegrationTests;

[TestClass]
public sealed class StorefrontCpuEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task CpuEndpoint_ReturnsDeterministicChecksum_And_EmitsCpuStages()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        await using StorefrontFactory factory = new(repositoryRoot);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage first = await client.GetAsync("/cpu?workFactor=2&iterations=20");
        HttpResponseMessage second = await client.GetAsync("/cpu?workFactor=2&iterations=20");

        Assert.AreEqual(System.Net.HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual(System.Net.HttpStatusCode.OK, second.StatusCode);

        JsonElement firstJson = JsonSerializer.Deserialize<JsonElement>(await first.Content.ReadAsStringAsync(), JsonOptions);
        JsonElement secondJson = JsonSerializer.Deserialize<JsonElement>(await second.Content.ReadAsStringAsync(), JsonOptions);

        string firstChecksum = firstJson.GetProperty("checksum").GetString()!;
        string secondChecksum = secondJson.GetProperty("checksum").GetString()!;
        string firstTraceId = firstJson.GetProperty("request").GetProperty("traceId").GetString()!;

        Assert.AreEqual(firstChecksum, secondChecksum);

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        Assert.HasCount(2, traces);

        RequestTraceTestHelper.TraceEnvelope firstTrace = traces.Single(trace => trace.Record.TraceId == firstTraceId);

        RequestTraceTestHelper.AssertRequiredFieldsPresent(firstTrace.Json);
        RequestTraceTestHelper.AssertMilestone1Trace(firstTrace.Record, "cpu-bound-lab", "/cpu", "GET", 200, expectedContractSatisfied: true);

        CollectionAssert.AreEquivalent(
            new[]
            {
                "request_received",
                "cpu_work_started",
                "cpu_work_completed",
                "response_sent",
                "http_request"
            },
            firstTrace.Record.StageTimings.Select(stage => stage.StageName).ToArray());

        Assert.AreEqual(firstChecksum, firstTrace.Record.StageTimings.Single(stage => stage.StageName == "cpu_work_completed").Metadata["checksum"]);
    }

    [TestMethod]
    public async Task CpuEndpoint_ValidationFailure_StillProducesWellFormedTrace()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        await using StorefrontFactory factory = new(repositoryRoot);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/cpu?workFactor=0&iterations=20");

        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, response.StatusCode);

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        Assert.HasCount(1, traces);

        RequestTraceTestHelper.TraceEnvelope trace = traces[0];
        RequestTraceTestHelper.AssertRequiredFieldsPresent(trace.Json);
        RequestTraceTestHelper.AssertMilestone1Trace(trace.Record, "cpu-bound-lab", "/cpu", "GET", 400, expectedContractSatisfied: false);

        CollectionAssert.AreEquivalent(
            new[]
            {
                "request_received",
                "response_sent",
                "http_request"
            },
            trace.Record.StageTimings.Select(stage => stage.StageName).ToArray());

        Assert.AreEqual("validation_failed", trace.Record.StageTimings.Single(stage => stage.StageName == "response_sent").Outcome);
        Assert.AreEqual("error", trace.Record.StageTimings.Single(stage => stage.StageName == "http_request").Outcome);
    }

    private static string CreateUniqueTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
