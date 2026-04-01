namespace Storefront.Api.LabEndpoints;

public static class IoWaitSimulator
{
    public const int MaxDelayMs = 30_000;
    public const int MaxJitterMs = 5_000;

    public static bool TryValidate(int delayMs, int jitterMs, out string? error)
    {
        if (delayMs < 0)
        {
            error = "delayMs must be zero or positive.";
            return false;
        }

        if (jitterMs < 0)
        {
            error = "jitterMs must be zero or positive.";
            return false;
        }

        if (delayMs > MaxDelayMs)
        {
            error = $"delayMs must be at or below {MaxDelayMs}.";
            return false;
        }

        if (jitterMs > MaxJitterMs)
        {
            error = $"jitterMs must be at or below {MaxJitterMs}.";
            return false;
        }

        error = null;
        return true;
    }

    public static IoWaitPlan CreatePlan(int delayMs, int jitterMs, string entropy)
    {
        if (!TryValidate(delayMs, jitterMs, out string? error))
        {
            throw new ArgumentOutOfRangeException(nameof(delayMs), error);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(entropy);

        int jitterOffsetMs = jitterMs == 0
            ? 0
            : ComputeBoundedOffset(delayMs, jitterMs, entropy);

        int appliedDelayMs = Math.Max(0, delayMs + jitterOffsetMs);

        return new IoWaitPlan(
            DelayMs: delayMs,
            JitterMs: jitterMs,
            JitterOffsetMs: jitterOffsetMs,
            AppliedDelayMs: appliedDelayMs);
    }

    public static Task WaitAsync(IoWaitPlan plan, CancellationToken cancellationToken = default) =>
        plan.AppliedDelayMs == 0
            ? Task.CompletedTask
            : Task.Delay(TimeSpan.FromMilliseconds(plan.AppliedDelayMs), cancellationToken);

    private static int ComputeBoundedOffset(int delayMs, int jitterMs, string entropy)
    {
        ulong hash = 1469598103934665603UL;

        foreach (char character in $"{delayMs}|{jitterMs}|{entropy}")
        {
            hash ^= character;
            hash *= 1099511628211UL;
        }

        int span = checked((jitterMs * 2) + 1);
        int offset = (int)(hash % (ulong)span) - jitterMs;
        return offset;
    }
}
