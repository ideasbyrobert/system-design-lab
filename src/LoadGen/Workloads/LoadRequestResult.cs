namespace LoadGenTool.Workloads;

public sealed record LoadRequestResult(
    string CorrelationId,
    int? StatusCode,
    double ElapsedMs,
    bool Succeeded,
    string? Error);
