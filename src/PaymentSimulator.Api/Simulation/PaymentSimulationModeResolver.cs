using Lab.Shared.Configuration;
using Lab.Shared.Http;
using Microsoft.AspNetCore.Http;

namespace PaymentSimulator.Api.Simulation;

internal static class PaymentSimulationModeResolver
{
    public static PaymentModeResolution Resolve(HttpRequest request, PaymentSimulatorOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        string? queryValue = Normalize(request.Query["mode"].ToString());

        if (queryValue is not null && !TryParse(queryValue, out PaymentSimulationMode _))
        {
            return PaymentModeResolution.Invalid(queryValue, "query");
        }

        if (TryParse(queryValue, out PaymentSimulationMode queryMode))
        {
            return PaymentModeResolution.Success(queryMode, "query");
        }

        string? headerValue = Normalize(request.Headers[LabHeaderNames.PaymentSimulatorMode].ToString());

        if (headerValue is not null && !TryParse(headerValue, out PaymentSimulationMode _))
        {
            return PaymentModeResolution.Invalid(headerValue, "header");
        }

        if (TryParse(headerValue, out PaymentSimulationMode headerMode))
        {
            return PaymentModeResolution.Success(headerMode, "header");
        }

        if (TryParse(options.DefaultMode, out PaymentSimulationMode defaultMode))
        {
            return PaymentModeResolution.Success(defaultMode, "default");
        }

        return PaymentModeResolution.Success(PaymentSimulationMode.FastSuccess, "fallback");
    }

    public static bool TryParse(string? rawValue, out PaymentSimulationMode mode)
    {
        string normalized = NormalizeKey(rawValue);

        switch (normalized)
        {
            case "fastsuccess":
                mode = PaymentSimulationMode.FastSuccess;
                return true;
            case "slowsuccess":
                mode = PaymentSimulationMode.SlowSuccess;
                return true;
            case "timeout":
                mode = PaymentSimulationMode.Timeout;
                return true;
            case "transientfailure":
                mode = PaymentSimulationMode.TransientFailure;
                return true;
            case "duplicatecallback":
                mode = PaymentSimulationMode.DuplicateCallback;
                return true;
            case "delayedconfirmation":
                mode = PaymentSimulationMode.DelayedConfirmation;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    public static string ToExternalText(PaymentSimulationMode mode) =>
        mode switch
        {
            PaymentSimulationMode.FastSuccess => "fast_success",
            PaymentSimulationMode.SlowSuccess => "slow_success",
            PaymentSimulationMode.Timeout => "timeout",
            PaymentSimulationMode.TransientFailure => "transient_failure",
            PaymentSimulationMode.DuplicateCallback => "duplicate_callback",
            PaymentSimulationMode.DelayedConfirmation => "delayed_confirmation",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported payment simulation mode.")
        };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}

internal sealed record PaymentModeResolution(
    PaymentSimulationMode Mode,
    string Source,
    bool IsValid,
    string? RawValue)
{
    public static PaymentModeResolution Success(PaymentSimulationMode mode, string source) =>
        new(mode, source, true, null);

    public static PaymentModeResolution Invalid(string rawValue, string source) =>
        new(default, source, false, rawValue);
}
