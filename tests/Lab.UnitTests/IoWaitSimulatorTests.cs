using Storefront.Api.LabEndpoints;

namespace Lab.UnitTests;

[TestClass]
public sealed class IoWaitSimulatorTests
{
    [TestMethod]
    public void CreatePlan_WithSameInputsAndEntropy_ReturnsSameAppliedDelay()
    {
        IoWaitPlan first = IoWaitSimulator.CreatePlan(delayMs: 75, jitterMs: 10, entropy: "trace-001");
        IoWaitPlan second = IoWaitSimulator.CreatePlan(delayMs: 75, jitterMs: 10, entropy: "trace-001");

        Assert.AreEqual(first.AppliedDelayMs, second.AppliedDelayMs);
        Assert.AreEqual(first.JitterOffsetMs, second.JitterOffsetMs);
        Assert.AreEqual(75, first.DelayMs);
        Assert.AreEqual(10, first.JitterMs);
        Assert.IsGreaterThanOrEqualTo(65, first.AppliedDelayMs);
        Assert.IsLessThanOrEqualTo(85, first.AppliedDelayMs);
    }

    [TestMethod]
    public void TryValidate_RejectsNegativeAndOversizedValues()
    {
        Assert.IsFalse(IoWaitSimulator.TryValidate(delayMs: -1, jitterMs: 0, out _));
        Assert.IsFalse(IoWaitSimulator.TryValidate(delayMs: 0, jitterMs: -1, out _));
        Assert.IsFalse(IoWaitSimulator.TryValidate(delayMs: IoWaitSimulator.MaxDelayMs + 1, jitterMs: 0, out _));
        Assert.IsFalse(IoWaitSimulator.TryValidate(delayMs: 0, jitterMs: IoWaitSimulator.MaxJitterMs + 1, out _));
    }
}
