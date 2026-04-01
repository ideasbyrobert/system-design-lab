namespace Storefront.Api.LabEndpoints;

public sealed record CpuWorkResult(
    int WorkFactor,
    int Iterations,
    long TotalMixOperations,
    string Checksum);
