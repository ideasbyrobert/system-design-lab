namespace Worker.Jobs;

internal sealed record WorkerJobExecutionResult(
    WorkerJobDisposition Disposition,
    bool ContractSatisfied,
    string RunId,
    string? ErrorCode = null,
    string? ErrorDetail = null,
    DateTimeOffset? NextAttemptUtc = null,
    string? UpdatedPayloadJson = null);
