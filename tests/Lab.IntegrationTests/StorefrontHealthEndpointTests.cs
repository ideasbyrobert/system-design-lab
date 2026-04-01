using System.Text.Json;
using Lab.Shared.Http;
using Lab.Telemetry.RequestTracing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Lab.IntegrationTests;

[TestClass]
public sealed class StorefrontHealthEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task HealthEndpoint_Returns200_And_ProducesARequestTrace()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        await using StorefrontFactory factory = new(repositoryRoot);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health");

        Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Headers.Contains(LabHeaderNames.RequestId));
        Assert.IsTrue(response.Headers.Contains(LabHeaderNames.TraceId));
        Assert.IsTrue(response.Headers.Contains(LabHeaderNames.RunId));
        Assert.IsTrue(response.Headers.Contains(LabHeaderNames.CorrelationId));

        string body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "\"status\":\"ok\"");

        string requestsPath = Path.Combine(repositoryRoot, "logs", "requests.jsonl");
        Assert.IsTrue(File.Exists(requestsPath));

        string[] lines = await File.ReadAllLinesAsync(requestsPath);
        Assert.HasCount(1, lines);

        RequestTraceRecord? record = JsonSerializer.Deserialize<RequestTraceRecord>(lines[0], JsonOptions);

        Assert.IsNotNull(record);
        Assert.AreEqual("health-check", record.Operation);
        Assert.AreEqual("/health", record.Route);
        Assert.AreEqual(200, record.StatusCode);
        Assert.IsTrue(record.ContractSatisfied);
        Assert.AreEqual(response.Headers.GetValues(LabHeaderNames.RunId).Single(), record.RunId);
        Assert.AreEqual(response.Headers.GetValues(LabHeaderNames.TraceId).Single(), record.TraceId);
        Assert.AreEqual(response.Headers.GetValues(LabHeaderNames.RequestId).Single(), record.RequestId);
        Assert.AreEqual(response.Headers.GetValues(LabHeaderNames.CorrelationId).Single(), record.CorrelationId);
    }

    private static string CreateUniqueTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

}
