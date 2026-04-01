using System.Text.Json;
using Lab.Telemetry.RequestTracing;

namespace Lab.IntegrationTests;

internal static class RequestTraceTestHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public sealed record TraceEnvelope(string Json, RequestTraceRecord Record);

    public static async Task<IReadOnlyList<TraceEnvelope>> ReadRequestTracesAsync(string repositoryRoot)
    {
        string requestsPath = Path.Combine(repositoryRoot, "logs", "requests.jsonl");
        Assert.IsTrue(File.Exists(requestsPath), $"Expected request trace file at '{requestsPath}'.");

        string[] lines = await File.ReadAllLinesAsync(requestsPath);

        return lines
            .Select(line => new TraceEnvelope(
                line,
                JsonSerializer.Deserialize<RequestTraceRecord>(line, JsonOptions)
                    ?? throw new InvalidOperationException("Request trace JSON could not be deserialized.")))
            .ToArray();
    }

    public static void AssertRequiredFieldsPresent(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        AssertHasString(root, "runId");
        AssertHasString(root, "traceId");
        AssertHasString(root, "requestId");
        AssertHasString(root, "operation");
        AssertHasString(root, "region");
        AssertHasString(root, "service");
        AssertHasString(root, "route");
        AssertHasString(root, "method");
        AssertHasNumber(root, "statusCode");

        Assert.IsTrue(root.TryGetProperty("contractSatisfied", out JsonElement contractSatisfied));
        Assert.IsTrue(
            contractSatisfied.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "Expected 'contractSatisfied' to be a JSON boolean.");

        Assert.IsTrue(root.TryGetProperty("stageTimings", out JsonElement stageTimings));
        Assert.AreEqual(JsonValueKind.Array, stageTimings.ValueKind);
    }

    public static void AssertMilestone1Trace(
        RequestTraceRecord trace,
        string expectedOperation,
        string expectedRoute,
        string expectedMethod,
        int expectedStatusCode,
        bool expectedContractSatisfied)
    {
        Assert.AreEqual(expectedOperation, trace.Operation);
        Assert.AreEqual(expectedRoute, trace.Route);
        Assert.AreEqual(expectedMethod, trace.Method);
        Assert.AreEqual(expectedStatusCode, trace.StatusCode);
        Assert.AreEqual(expectedContractSatisfied, trace.ContractSatisfied);
        Assert.IsFalse(string.IsNullOrWhiteSpace(trace.Region));
        Assert.IsFalse(string.IsNullOrWhiteSpace(trace.RunId));
        Assert.IsFalse(string.IsNullOrWhiteSpace(trace.TraceId));
        Assert.IsFalse(string.IsNullOrWhiteSpace(trace.RequestId));
        Assert.IsGreaterThan(0, trace.StageTimings.Count);
    }

    private static void AssertHasString(JsonElement root, string propertyName)
    {
        Assert.IsTrue(root.TryGetProperty(propertyName, out JsonElement value), $"Expected property '{propertyName}'.");
        Assert.AreEqual(JsonValueKind.String, value.ValueKind);
        Assert.IsFalse(string.IsNullOrWhiteSpace(value.GetString()), $"Expected property '{propertyName}' to be non-empty.");
    }

    private static void AssertHasNumber(JsonElement root, string propertyName)
    {
        Assert.IsTrue(root.TryGetProperty(propertyName, out JsonElement value), $"Expected property '{propertyName}'.");
        Assert.AreEqual(JsonValueKind.Number, value.ValueKind);
    }
}
