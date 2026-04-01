namespace Lab.Shared.Http;

public static class LabHeaderNames
{
    public const string IdempotencyKey = "Idempotency-Key";

    public const string RunId = "X-Run-Id";

    public const string CorrelationId = "X-Correlation-Id";

    public const string SessionKey = "X-Session-Key";

    public const string PaymentSimulatorMode = "X-Payment-Simulator-Mode";

    public const string RequestId = "X-Request-Id";

    public const string TraceId = "X-Trace-Id";

    public const string DebugTelemetry = "X-Debug-Telemetry";
}
