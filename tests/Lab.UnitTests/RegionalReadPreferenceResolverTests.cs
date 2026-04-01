using Lab.Shared.Configuration;
using Lab.Shared.RegionalReads;

namespace Lab.UnitTests;

[TestClass]
public sealed class RegionalReadPreferenceResolverTests
{
    private static readonly RegionOptions RegionOptions = new()
    {
        PrimaryRegion = "us-east",
        EastReplicaRegion = "us-east",
        WestReplicaRegion = "us-west"
    };

    [TestMethod]
    public void ResolveProductRead_LocalInWestRegion_UsesWestReplicaWithoutFallback()
    {
        RegionalReadPreference preference = RegionalProductReadPreferenceResolver.Resolve(
            currentRegion: "us-west",
            regionOptions: RegionOptions,
            requestedReadSource: "local");

        Assert.AreEqual("local", preference.RequestedReadSource);
        Assert.AreEqual("replica-west", preference.EffectiveReadSource);
        Assert.AreEqual("us-west", preference.TargetRegion);
        Assert.AreEqual("same-region", preference.SelectionScope);
        Assert.IsFalse(preference.FallbackApplied);
        Assert.IsNull(preference.FallbackReason);
    }

    [TestMethod]
    public void ResolveProductRead_LocalInUnknownRegion_FallsBackToPrimary()
    {
        RegionalReadPreference preference = RegionalProductReadPreferenceResolver.Resolve(
            currentRegion: "eu-central",
            regionOptions: RegionOptions,
            requestedReadSource: "local");

        Assert.AreEqual("local", preference.RequestedReadSource);
        Assert.AreEqual("primary", preference.EffectiveReadSource);
        Assert.AreEqual("us-east", preference.TargetRegion);
        Assert.AreEqual("cross-region", preference.SelectionScope);
        Assert.IsTrue(preference.FallbackApplied);
        Assert.AreEqual("no_local_product_read_source_for_region", preference.FallbackReason);
    }

    [TestMethod]
    public void ResolveProductRead_LocalReplicaUnavailable_FallsBackToPrimaryAcrossRegions()
    {
        RegionalReadPreference preference = RegionalProductReadPreferenceResolver.Resolve(
            currentRegion: "us-west",
            regionOptions: RegionOptions,
            requestedReadSource: "local",
            localReplicaAvailable: false);

        Assert.AreEqual("local", preference.RequestedReadSource);
        Assert.AreEqual("primary", preference.EffectiveReadSource);
        Assert.AreEqual("us-east", preference.TargetRegion);
        Assert.AreEqual("cross-region", preference.SelectionScope);
        Assert.IsTrue(preference.FallbackApplied);
        Assert.AreEqual("local_replica_unavailable", preference.FallbackReason);
    }

    [TestMethod]
    public void ResolveOrderHistoryRead_Local_UsesReadModelInCurrentRegion()
    {
        RegionalReadPreference preference = RegionalOrderHistoryReadPreferenceResolver.Resolve(
            currentRegion: "us-west",
            regionOptions: RegionOptions,
            requestedReadSource: "local");

        Assert.AreEqual("local", preference.RequestedReadSource);
        Assert.AreEqual("read-model", preference.EffectiveReadSource);
        Assert.AreEqual("us-west", preference.TargetRegion);
        Assert.AreEqual("same-region", preference.SelectionScope);
        Assert.IsFalse(preference.FallbackApplied);
        Assert.IsNull(preference.FallbackReason);
    }

    [TestMethod]
    public void CreatePrimaryProjectionFallback_MarksFallbackExplicitly()
    {
        RegionalReadPreference preference = RegionalOrderHistoryReadPreferenceResolver.CreatePrimaryProjectionFallback(
            currentRegion: "us-west",
            regionOptions: RegionOptions,
            requestedReadSource: "local",
            fallbackReason: "read_model_invalid");

        Assert.AreEqual("local", preference.RequestedReadSource);
        Assert.AreEqual("primary-projection", preference.EffectiveReadSource);
        Assert.AreEqual("us-east", preference.TargetRegion);
        Assert.AreEqual("cross-region", preference.SelectionScope);
        Assert.IsTrue(preference.FallbackApplied);
        Assert.AreEqual("read_model_invalid", preference.FallbackReason);
    }
}
