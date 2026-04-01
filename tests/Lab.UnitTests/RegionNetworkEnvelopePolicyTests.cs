using Lab.Shared.Configuration;
using Lab.Shared.Networking;
using Microsoft.Extensions.Options;

namespace Lab.UnitTests;

[TestClass]
public sealed class RegionNetworkEnvelopePolicyTests
{
    [TestMethod]
    public void Resolve_SameRegion_UsesConfiguredSameRegionLatency()
    {
        ConfiguredRegionNetworkEnvelopePolicy policy = CreatePolicy(
            currentRegion: "us-east",
            sameRegionLatencyMs: 2,
            crossRegionLatencyMs: 19);

        RegionNetworkEnvelope envelope = policy.Resolve("us-east");

        Assert.AreEqual("us-east", envelope.CallerRegion);
        Assert.AreEqual("us-east", envelope.TargetRegion);
        Assert.AreEqual("same-region", envelope.NetworkScope);
        Assert.AreEqual(2, envelope.InjectedDelayMs);
        Assert.IsFalse(envelope.IsCrossRegion);
    }

    [TestMethod]
    public void Resolve_CrossRegion_UsesConfiguredCrossRegionLatency()
    {
        ConfiguredRegionNetworkEnvelopePolicy policy = CreatePolicy(
            currentRegion: "us-east",
            sameRegionLatencyMs: 2,
            crossRegionLatencyMs: 19);

        RegionNetworkEnvelope envelope = policy.Resolve("us-west");

        Assert.AreEqual("us-east", envelope.CallerRegion);
        Assert.AreEqual("us-west", envelope.TargetRegion);
        Assert.AreEqual("cross-region", envelope.NetworkScope);
        Assert.AreEqual(19, envelope.InjectedDelayMs);
        Assert.IsTrue(envelope.IsCrossRegion);
    }

    [TestMethod]
    public void DependencyCallNetworkMetadata_IncludesEnvelopeFields_And_PreservesAdditionalMetadata()
    {
        RegionNetworkEnvelope envelope = new(
            CallerRegion: "us-east",
            TargetRegion: "us-west",
            NetworkScope: "cross-region",
            InjectedDelayMs: 17);

        IReadOnlyDictionary<string, string?> metadata = DependencyCallNetworkMetadata.Create(
            envelope,
            new Dictionary<string, string?>
            {
                ["readSource"] = "replica-east"
            });

        Assert.AreEqual("us-east", metadata["callerRegion"]);
        Assert.AreEqual("us-west", metadata["targetRegion"]);
        Assert.AreEqual("cross-region", metadata["networkScope"]);
        Assert.AreEqual("17", metadata["injectedDelayMs"]);
        Assert.AreEqual("replica-east", metadata["readSource"]);
    }

    private static ConfiguredRegionNetworkEnvelopePolicy CreatePolicy(
        string currentRegion,
        int sameRegionLatencyMs,
        int crossRegionLatencyMs)
    {
        EnvironmentLayout layout = new(
            RepositoryRoot: "/tmp/repo",
            SourceRoot: "/tmp/repo/src",
            ContentRoot: "/tmp/repo/src/Storefront.Api",
            ServiceName: "Storefront.Api",
            CurrentRegion: currentRegion,
            PrimaryDatabasePath: "/tmp/repo/data/primary.db",
            ReplicaEastDatabasePath: "/tmp/repo/data/replica-east.db",
            ReplicaWestDatabasePath: "/tmp/repo/data/replica-west.db",
            ReadModelDatabasePath: "/tmp/repo/data/readmodels.db",
            RequestsJsonlPath: "/tmp/repo/logs/requests.jsonl",
            JobsJsonlPath: "/tmp/repo/logs/jobs.jsonl",
            RunsDirectory: "/tmp/repo/logs/runs",
            AnalysisDirectory: "/tmp/repo/analysis",
            ServiceLogPath: "/tmp/repo/logs/storefront.log");

        return new ConfiguredRegionNetworkEnvelopePolicy(
            layout,
            Options.Create(new RegionOptions
            {
                CurrentRegion = currentRegion,
                SameRegionLatencyMs = sameRegionLatencyMs,
                CrossRegionLatencyMs = crossRegionLatencyMs
            }));
    }
}
