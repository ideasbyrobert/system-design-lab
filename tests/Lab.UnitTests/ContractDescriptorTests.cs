using Lab.Shared.Contracts;

namespace Lab.UnitTests;

[TestClass]
public sealed class ContractDescriptorTests
{
    [TestMethod]
    public void OperationContractDescriptor_Create_CopiesAndNormalizesCollections()
    {
        string[] inputs = [" productId ", " region "];
        string[] invariants = [" Storefront.Api is the boundary. "];

        OperationContractDescriptor descriptor = OperationContractDescriptor.Create(
            operationName: " product-page ",
            inputs: inputs,
            invariants: invariants,
            observationStart: " request arrives ",
            observationEnd: " response leaves ");

        inputs[0] = "changed";
        invariants[0] = "changed";

        Assert.AreEqual("product-page", descriptor.OperationName);
        CollectionAssert.AreEqual(new[] { "productId", "region" }, descriptor.Inputs.ToArray());
        CollectionAssert.AreEqual(new[] { "Storefront.Api is the boundary." }, descriptor.Invariants.ToArray());
        Assert.AreEqual("request arrives", descriptor.ObservationStart);
        Assert.AreEqual("response leaves", descriptor.ObservationEnd);
    }

    [TestMethod]
    public void BusinessOperationContracts_ExposeTheInitialFourOperations()
    {
        string[] operationNames = BusinessOperationContracts.All
            .Select(contract => contract.OperationName)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "product-page",
                "add-item-to-cart",
                "checkout-sync",
                "order-history"
            },
            operationNames);

        Assert.IsTrue(BusinessOperationContracts.ProductPage.ObservationStart.Contains("Storefront.Api", StringComparison.Ordinal));
        Assert.IsTrue(BusinessOperationContracts.ProductPage.ObservationEnd.Contains("HTTP response", StringComparison.Ordinal));
        Assert.IsGreaterThanOrEqualTo(2, BusinessOperationContracts.CheckoutSync.Postconditions.Count);
        Assert.IsGreaterThanOrEqualTo(2, BusinessOperationContracts.OrderHistory.Invariants.Count);
    }
}
