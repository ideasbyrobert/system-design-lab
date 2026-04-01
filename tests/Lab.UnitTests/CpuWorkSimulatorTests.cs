using Storefront.Api.LabEndpoints;

namespace Lab.UnitTests;

[TestClass]
public sealed class CpuWorkSimulatorTests
{
    [TestMethod]
    public void Execute_WithSameInputs_ReturnsSameChecksumAndOperationCount()
    {
        CpuWorkResult first = CpuWorkSimulator.Execute(workFactor: 4, iterations: 25);
        CpuWorkResult second = CpuWorkSimulator.Execute(workFactor: 4, iterations: 25);

        Assert.AreEqual(first.TotalMixOperations, second.TotalMixOperations);
        Assert.AreEqual(first.Checksum, second.Checksum);
    }

    [TestMethod]
    public void TryValidate_RejectsOversizedWorkloads()
    {
        bool valid = CpuWorkSimulator.TryValidate(workFactor: 500, iterations: 500, out string? error);

        Assert.IsFalse(valid);
        Assert.IsNotNull(error);
        StringAssert.Contains(error, "too large");
    }
}
