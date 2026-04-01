using System.Numerics;

namespace Storefront.Api.LabEndpoints;

public static class CpuWorkSimulator
{
    public const int InnerRounds = 512;
    public const long MaxTotalMixOperations = 50_000_000;

    public static bool TryValidate(int workFactor, int iterations, out string? error)
    {
        if (workFactor <= 0)
        {
            error = "workFactor must be positive.";
            return false;
        }

        if (iterations <= 0)
        {
            error = "iterations must be positive.";
            return false;
        }

        long totalMixOperations;

        try
        {
            totalMixOperations = checked((long)workFactor * iterations * InnerRounds);
        }
        catch (OverflowException)
        {
            error = "The selected work exceeds the supported range.";
            return false;
        }

        if (totalMixOperations > MaxTotalMixOperations)
        {
            error = $"The selected work is too large. Keep total mix operations at or below {MaxTotalMixOperations}.";
            return false;
        }

        error = null;
        return true;
    }

    public static CpuWorkResult Execute(int workFactor, int iterations)
    {
        if (!TryValidate(workFactor, iterations, out string? error))
        {
            throw new ArgumentOutOfRangeException(nameof(workFactor), error);
        }

        long totalMixOperations = checked((long)workFactor * iterations * InnerRounds);
        int roundsPerIteration = checked(workFactor * InnerRounds);

        ulong state = 1469598103934665603UL ^ (ulong)workFactor ^ ((ulong)iterations << 17);

        for (int iterationIndex = 0; iterationIndex < iterations; iterationIndex++)
        {
            ulong local = state ^ ((ulong)(iterationIndex + 1) * 0x9E3779B185EBCA87UL);

            for (int roundIndex = 0; roundIndex < roundsPerIteration; roundIndex++)
            {
                ulong roundSalt = ((ulong)(roundIndex + 1) * 0x100000001B3UL) ^ (ulong)iterationIndex;
                local ^= roundSalt;
                local = BitOperations.RotateLeft(local, (roundIndex & 31) + 1);
                local *= 0x9E3779B185EBCA87UL;
                local ^= local >> 29;
            }

            state ^= local + (ulong)iterationIndex;
            state = BitOperations.RotateLeft(state, (iterationIndex & 15) + 1);
            state *= 1099511628211UL;
            state ^= state >> 33;
        }

        return new CpuWorkResult(
            workFactor,
            iterations,
            totalMixOperations,
            state.ToString("X16", System.Globalization.CultureInfo.InvariantCulture));
    }
}
