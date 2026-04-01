namespace Lab.Shared.Configuration;

public sealed class QueueOptions
{
    public int LeaseDurationSeconds { get; set; } = 30;

    public int PollIntervalMilliseconds { get; set; } = 250;

    public int MaxDequeueBatchSize { get; set; } = 16;

    public int MaxRetryAttempts { get; set; } = 3;
}
