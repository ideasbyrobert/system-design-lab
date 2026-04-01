using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lab.Shared.Http;
using Lab.Telemetry.RequestTracing;

namespace Lab.IntegrationTests;

[TestClass]
public sealed class PaymentSimulatorEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task Authorize_FastSuccess_UsesQueryMode_AndRecordsTrace()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        await using PaymentSimulatorFactory factory = new(repositoryRoot, CreateFastTestOverrides());
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/payments/authorize?mode=fast_success",
            new
            {
                paymentId = "pay-fast-041",
                orderId = "order-fast-041",
                amountCents = 1500,
                currency = "USD"
            });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("fast_success", response.Headers.GetValues(LabHeaderNames.PaymentSimulatorMode).Single());

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
        string traceId = body.GetProperty("request").GetProperty("traceId").GetString()!;

        Assert.AreEqual("authorized", body.GetProperty("outcome").GetString());
        Assert.AreEqual("fast_success", body.GetProperty("mode").GetString());
        Assert.AreEqual("query", body.GetProperty("modeSource").GetString());
        Assert.AreEqual("psim-pay-fast-041-0001", body.GetProperty("providerReference").GetString());
        Assert.IsFalse(body.GetProperty("callbackPending").GetBoolean());
        Assert.AreEqual(0, body.GetProperty("callbackCountScheduled").GetInt32());

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceRecord trace = traces.Single(item => item.Record.TraceId == traceId).Record;

        Assert.AreEqual("payment-authorize", trace.Operation);
        Assert.AreEqual("/payments/authorize", trace.Route);
        Assert.AreEqual("POST", trace.Method);
        Assert.AreEqual(200, trace.StatusCode);
        Assert.IsTrue(trace.ContractSatisfied);
        Assert.AreEqual("success", trace.StageTimings.Single(stage => stage.StageName == "response_sent").Outcome);
    }

    [TestMethod]
    public async Task Authorize_SlowSuccess_UsesHeaderMode_AndConsumesConfiguredDelay()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        await using PaymentSimulatorFactory factory = new(repositoryRoot, CreateFastTestOverrides());
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Post, "/payments/authorize")
        {
            Content = JsonContent.Create(new
            {
                paymentId = "pay-slow-041",
                orderId = "order-slow-041",
                amountCents = 1500,
                currency = "USD"
            })
        };
        request.Headers.TryAddWithoutValidation(LabHeaderNames.PaymentSimulatorMode, "slow_success");

        Stopwatch stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response = await client.SendAsync(request);
        stopwatch.Stop();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("slow_success", response.Headers.GetValues(LabHeaderNames.PaymentSimulatorMode).Single());
        Assert.IsGreaterThanOrEqualTo(45L, stopwatch.ElapsedMilliseconds);

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
        Assert.AreEqual("slow_success", body.GetProperty("mode").GetString());
        Assert.AreEqual("header", body.GetProperty("modeSource").GetString());
        Assert.AreEqual("authorized", body.GetProperty("outcome").GetString());
    }

    [TestMethod]
    public async Task Authorize_Timeout_Returns504_WithDeterministicFailure()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        await using PaymentSimulatorFactory factory = new(repositoryRoot, CreateFastTestOverrides());
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Post, "/payments/authorize")
        {
            Content = JsonContent.Create(new
            {
                paymentId = "pay-timeout-041",
                orderId = "order-timeout-041",
                amountCents = 1500,
                currency = "USD"
            })
        };
        request.Headers.TryAddWithoutValidation(LabHeaderNames.PaymentSimulatorMode, "timeout");

        HttpResponseMessage response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.GatewayTimeout, response.StatusCode);

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
        string traceId = body.GetProperty("request").GetProperty("traceId").GetString()!;

        Assert.AreEqual("simulated_timeout", body.GetProperty("error").GetString());
        Assert.AreEqual("timeout", body.GetProperty("mode").GetString());
        Assert.AreEqual("psim-pay-timeout-041-0001", body.GetProperty("providerReference").GetString());

        IReadOnlyList<RequestTraceTestHelper.TraceEnvelope> traces = await RequestTraceTestHelper.ReadRequestTracesAsync(repositoryRoot);
        RequestTraceRecord trace = traces.Single(item => item.Record.TraceId == traceId).Record;

        Assert.AreEqual("simulated_timeout", trace.ErrorCode);
        Assert.IsTrue(trace.ContractSatisfied);
        Assert.AreEqual(504, trace.StatusCode);
        Assert.AreEqual("simulated_failure", trace.StageTimings.Single(stage => stage.StageName == "response_sent").Outcome);
    }

    [TestMethod]
    public async Task Authorize_TransientFailure_FailsFirstAttempt_ThenSucceeds()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        await using PaymentSimulatorFactory factory = new(repositoryRoot, CreateFastTestOverrides());
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage first = await client.PostAsJsonAsync(
            "/payments/authorize?mode=transient_failure",
            new
            {
                paymentId = "pay-transient-041",
                orderId = "order-transient-041",
                amountCents = 1500,
                currency = "USD"
            });

        HttpResponseMessage second = await client.PostAsJsonAsync(
            "/payments/authorize?mode=transient_failure",
            new
            {
                paymentId = "pay-transient-041",
                orderId = "order-transient-041",
                amountCents = 1500,
                currency = "USD"
            });

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);

        JsonElement firstBody = JsonSerializer.Deserialize<JsonElement>(await first.Content.ReadAsStringAsync(), JsonOptions);
        JsonElement secondBody = JsonSerializer.Deserialize<JsonElement>(await second.Content.ReadAsStringAsync(), JsonOptions);

        Assert.AreEqual("simulated_transient_failure", firstBody.GetProperty("error").GetString());
        Assert.AreEqual(1, firstBody.GetProperty("attemptNumber").GetInt32());
        Assert.AreEqual("psim-pay-transient-041-0001", firstBody.GetProperty("providerReference").GetString());
        Assert.AreEqual("authorized", secondBody.GetProperty("outcome").GetString());
        Assert.AreEqual(2, secondBody.GetProperty("attemptNumber").GetInt32());
        Assert.AreEqual("psim-pay-transient-041-0002", secondBody.GetProperty("providerReference").GetString());

        HttpResponseMessage statusResponse = await client.GetAsync("/payments/authorizations/pay-transient-041");
        Assert.AreEqual(HttpStatusCode.OK, statusResponse.StatusCode);
        JsonElement statusBody = JsonSerializer.Deserialize<JsonElement>(await statusResponse.Content.ReadAsStringAsync(), JsonOptions);
        Assert.AreEqual(2, statusBody.GetProperty("attemptCount").GetInt32());
        Assert.AreEqual("authorized", statusBody.GetProperty("outcome").GetString());
        Assert.AreEqual("psim-pay-transient-041-0002", statusBody.GetProperty("latestProviderReference").GetString());
    }

    [TestMethod]
    public async Task Authorize_DelayedConfirmation_SchedulesOneCallback()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        await using PaymentSimulatorFactory factory = new(repositoryRoot, CreateFastTestOverrides());
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/payments/authorize?mode=delayed_confirmation",
            new
            {
                paymentId = "pay-delayed-041",
                orderId = "order-delayed-041",
                amountCents = 1500,
                currency = "USD"
            });

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
        Assert.AreEqual("pending_confirmation", body.GetProperty("outcome").GetString());
        Assert.AreEqual(1, body.GetProperty("callbackCountScheduled").GetInt32());

        JsonElement statusBody = await PollForStatusAsync(
            client,
            "pay-delayed-041",
            body =>
                body.GetProperty("callbacks").GetArrayLength() == 1 &&
                body.GetProperty("callbacks")[0].GetProperty("status").GetString() == "SkippedNoTarget");

        Assert.AreEqual(1, statusBody.GetProperty("attemptCount").GetInt32());
        Assert.AreEqual("pending_confirmation", statusBody.GetProperty("outcome").GetString());
        Assert.AreEqual(1, statusBody.GetProperty("callbacks").GetArrayLength());
    }

    [TestMethod]
    public async Task Authorize_DuplicateCallback_SchedulesTwoCallbacks()
    {
        string repositoryRoot = CreateUniqueTempDirectory();

        await using PaymentSimulatorFactory factory = new(repositoryRoot, CreateFastTestOverrides());
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/payments/authorize?mode=duplicate_callback",
            new
            {
                paymentId = "pay-duplicate-041",
                orderId = "order-duplicate-041",
                amountCents = 1500,
                currency = "USD"
            });

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);

        JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
        Assert.AreEqual("pending_confirmation", body.GetProperty("outcome").GetString());
        Assert.AreEqual(2, body.GetProperty("callbackCountScheduled").GetInt32());

        JsonElement statusBody = await PollForStatusAsync(
            client,
            "pay-duplicate-041",
            snapshot =>
                snapshot.GetProperty("callbacks").GetArrayLength() == 2 &&
                snapshot.GetProperty("callbacks").EnumerateArray().All(item => item.GetProperty("status").GetString() == "SkippedNoTarget"));

        Assert.AreEqual(2, statusBody.GetProperty("callbacks").GetArrayLength());
        CollectionAssert.AreEqual(
            new[] { 1, 2 },
            statusBody.GetProperty("callbacks")
                .EnumerateArray()
                .Select(item => item.GetProperty("sequenceNumber").GetInt32())
                .ToArray());
    }

    private static async Task<JsonElement> PollForStatusAsync(
        HttpClient client,
        string paymentId,
        Func<JsonElement, bool> predicate)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);

        while (DateTime.UtcNow < deadline)
        {
            HttpResponseMessage response = await client.GetAsync($"/payments/authorizations/{paymentId}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                JsonElement body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);
                if (predicate(body))
                {
                    return body;
                }
            }

            await Task.Delay(25);
        }

        Assert.Fail($"Timed out waiting for payment simulator status '{paymentId}'.");
        return default;
    }

    private static IReadOnlyDictionary<string, string?> CreateFastTestOverrides() =>
        new Dictionary<string, string?>
        {
            ["Lab:PaymentSimulator:DefaultMode"] = "FastSuccess",
            ["Lab:PaymentSimulator:FastLatencyMilliseconds"] = "5",
            ["Lab:PaymentSimulator:SlowLatencyMilliseconds"] = "60",
            ["Lab:PaymentSimulator:TimeoutLatencyMilliseconds"] = "90",
            ["Lab:PaymentSimulator:DelayedConfirmationMilliseconds"] = "75",
            ["Lab:PaymentSimulator:DuplicateCallbackSpacingMilliseconds"] = "25",
            ["Lab:PaymentSimulator:DispatcherPollMilliseconds"] = "10"
        };

    private static string CreateUniqueTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
