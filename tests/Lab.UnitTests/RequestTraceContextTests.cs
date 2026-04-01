using Lab.Shared.Contracts;
using Lab.Telemetry.RequestTracing;

namespace Lab.UnitTests;

[TestClass]
public sealed class RequestTraceContextTests
{
    [TestMethod]
    public void BeginRequest_AccumulatesStagesDependenciesFlagsAndFinalRecord()
    {
        ManualTimeProvider timeProvider = new(new DateTimeOffset(2026, 03, 31, 12, 0, 0, TimeSpan.Zero));
        RequestTraceFactory factory = new("Storefront.Api", "local", timeProvider, "run-001");

        RequestTraceContext trace = factory.BeginRequest(
            contract: BusinessOperationContracts.ProductPage,
            route: "/products/sku-123",
            method: "GET",
            requestId: "req-001",
            correlationId: "corr-001");

        timeProvider.Advance(TimeSpan.FromMilliseconds(5));

        using (RequestTraceContext.StageTraceScope stage = trace.BeginStage(
            "load_product",
            new Dictionary<string, string?> { ["source"] = "projection" }))
        {
            timeProvider.Advance(TimeSpan.FromMilliseconds(8));
            stage.Complete("success");
        }

        timeProvider.Advance(TimeSpan.FromMilliseconds(2));

        using (RequestTraceContext.DependencyCallScope dependency = trace.BeginDependencyCall(
            dependencyName: "catalog-api",
            route: "/catalog/products/sku-123",
            region: "local",
            metadata: new Dictionary<string, string?> { ["phase"] = "read-through" },
            notes: ["read-through"]))
        {
            timeProvider.Advance(TimeSpan.FromMilliseconds(7));
            dependency.Complete(
                statusCode: 200,
                outcome: "success",
                metadata: new Dictionary<string, string?> { ["cache"] = "miss" },
                notes: ["cache-miss"]);
        }

        trace.MarkContractSatisfied();
        trace.MarkCacheHit();
        trace.SetSessionKey("sess-unit-001");
        trace.SetReadSource("replica-east");
        trace.SetFreshnessMetrics(comparedCount: 1, staleCount: 1, maxStalenessAgeMs: 125d);
        trace.AddNote("unit-test");

        timeProvider.Advance(TimeSpan.FromMilliseconds(3));

        RequestTraceRecord record = trace.Complete(statusCode: 200);

        Assert.AreEqual("run-001", record.RunId);
        Assert.AreEqual("product-page", record.Operation);
        Assert.AreEqual("local", record.Region);
        Assert.AreEqual("Storefront.Api", record.Service);
        Assert.AreEqual("/products/sku-123", record.Route);
        Assert.AreEqual("GET", record.Method);
        Assert.AreEqual(200, record.StatusCode);
        Assert.AreEqual(25d, record.LatencyMs, 0.0001d);
        Assert.IsTrue(record.ContractSatisfied);
        Assert.IsTrue(record.CacheHit);
        Assert.IsFalse(record.RateLimited);
        Assert.AreEqual("sess-unit-001", record.SessionKey);
        Assert.AreEqual("corr-001", record.CorrelationId);
        Assert.AreEqual("replica-east", record.ReadSource);
        Assert.AreEqual(1, record.FreshnessComparedCount);
        Assert.AreEqual(1, record.FreshnessStaleCount);
        Assert.AreEqual(1d, record.FreshnessStaleFraction!.Value, 0.0001d);
        Assert.AreEqual(125d, record.MaxStalenessAgeMs!.Value, 0.0001d);
        Assert.HasCount(1, record.StageTimings);
        Assert.HasCount(1, record.DependencyCalls);
        Assert.AreEqual(8d, record.StageTimings[0].ElapsedMs, 0.0001d);
        Assert.AreEqual("projection", record.StageTimings[0].Metadata["source"]);
        Assert.AreEqual(7d, record.DependencyCalls[0].ElapsedMs, 0.0001d);
        Assert.AreEqual("read-through", record.DependencyCalls[0].Metadata["phase"]);
        Assert.AreEqual("miss", record.DependencyCalls[0].Metadata["cache"]);
        CollectionAssert.AreEqual(new[] { "read-through", "cache-miss" }, record.DependencyCalls[0].Notes.ToArray());
        CollectionAssert.AreEqual(new[] { "unit-test" }, record.Notes.ToArray());
    }

    [TestMethod]
    public void Complete_ClosesTheTraceAgainstFurtherMutation()
    {
        ManualTimeProvider timeProvider = new(new DateTimeOffset(2026, 03, 31, 12, 0, 0, TimeSpan.Zero));
        RequestTraceFactory factory = new("Storefront.Api", "local", timeProvider, "run-002");
        RequestTraceContext trace = factory.BeginRequest(
            contract: BusinessOperationContracts.ProductPage,
            route: "/",
            method: "GET",
            requestId: "req-002");

        RequestTraceRecord _ = trace.Complete(statusCode: 200);

        Assert.ThrowsExactly<InvalidOperationException>(() => trace.AddNote("late note"));
        Assert.ThrowsExactly<InvalidOperationException>(() => trace.Complete(statusCode: 200));
    }

    [TestMethod]
    public void BeginRequest_UsesExplicitRunIdWhenTheCallerSuppliesOne()
    {
        ManualTimeProvider timeProvider = new(new DateTimeOffset(2026, 03, 31, 12, 0, 0, TimeSpan.Zero));
        RequestTraceFactory factory = new("Storefront.Api", "local", timeProvider, "default-run");

        RequestTraceContext trace = factory.BeginRequest(
            contract: BusinessOperationContracts.ProductPage,
            route: "/",
            method: "GET",
            requestId: "req-003",
            runId: "load-test-run");

        RequestTraceRecord record = trace.Complete(statusCode: 200);

        Assert.AreEqual("load-test-run", record.RunId);
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private long _timestamp;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => utcNow;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan delta)
        {
            utcNow = utcNow.Add(delta);
            _timestamp += delta.Ticks;
        }
    }
}
