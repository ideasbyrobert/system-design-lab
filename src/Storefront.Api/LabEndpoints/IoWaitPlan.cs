namespace Storefront.Api.LabEndpoints;

public sealed record IoWaitPlan(
    int DelayMs,
    int JitterMs,
    int JitterOffsetMs,
    int AppliedDelayMs);
