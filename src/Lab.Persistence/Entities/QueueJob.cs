namespace Lab.Persistence.Entities;

public sealed class QueueJob
{
    public string QueueJobId { get; set; } = string.Empty;

    public string JobType { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset AvailableUtc { get; set; }

    public DateTimeOffset EnqueuedUtc { get; set; }

    public string? LeaseOwner { get; set; }

    public DateTimeOffset? LeaseExpiresUtc { get; set; }

    public DateTimeOffset? StartedUtc { get; set; }

    public DateTimeOffset? CompletedUtc { get; set; }

    public int RetryCount { get; set; }

    public string? LastError { get; set; }
}
