using Lab.Telemetry.RequestTracing;

namespace Lab.Analysis.Models;

public sealed record AnalysisFilter(
    string? RunId,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    string? Operation = null)
{
    public static AnalysisFilter None { get; } = new(null, null, null, null);

    public bool Matches(RequestTraceRecord traceRecord)
    {
        ArgumentNullException.ThrowIfNull(traceRecord);

        if (!string.IsNullOrWhiteSpace(RunId) &&
            !string.Equals(traceRecord.RunId, RunId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(Operation) &&
            !string.Equals(traceRecord.Operation, Operation, StringComparison.Ordinal))
        {
            return false;
        }

        return OverlapsWindow(traceRecord.ArrivalUtc, traceRecord.CompletionUtc);
    }

    public bool Matches(JobTraceRecord traceRecord)
    {
        ArgumentNullException.ThrowIfNull(traceRecord);

        if (!string.IsNullOrWhiteSpace(RunId) &&
            !string.Equals(traceRecord.RunId, RunId, StringComparison.Ordinal))
        {
            return false;
        }

        DateTimeOffset end = traceRecord.ExecutionEndUtc
            ?? traceRecord.ExecutionStartUtc
            ?? traceRecord.DequeuedUtc
            ?? traceRecord.EnqueuedUtc;

        return OverlapsWindow(traceRecord.EnqueuedUtc, end);
    }

    private bool OverlapsWindow(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        if (FromUtc.HasValue && endUtc < FromUtc.Value)
        {
            return false;
        }

        if (ToUtc.HasValue && startUtc > ToUtc.Value)
        {
            return false;
        }

        return true;
    }
}
