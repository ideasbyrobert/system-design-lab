using Lab.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace Worker;

public sealed class BackgroundWorker(
    WorkerQueueProcessor processor,
    IOptions<QueueOptions> queueOptions) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int processedCount = await processor.ProcessAvailableJobsAsync(stoppingToken);

            if (processedCount == 0)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(Math.Max(1, queueOptions.Value.PollIntervalMilliseconds)),
                    stoppingToken);
            }
        }
    }
}
