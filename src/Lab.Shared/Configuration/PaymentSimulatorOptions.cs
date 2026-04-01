namespace Lab.Shared.Configuration;

public sealed class PaymentSimulatorOptions
{
    public string DefaultMode { get; set; } = "FastSuccess";

    public int FastLatencyMilliseconds { get; set; } = 25;

    public int SlowLatencyMilliseconds { get; set; } = 500;

    public int TimeoutLatencyMilliseconds { get; set; } = 3000;

    public int DelayedConfirmationMilliseconds { get; set; } = 1000;

    public int DuplicateCallbackSpacingMilliseconds { get; set; } = 100;

    public int DispatcherPollMilliseconds { get; set; } = 25;
}
