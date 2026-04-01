using System.Net.Http.Json;
using Lab.Shared.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaymentSimulator.Api.Simulation;

internal sealed class PaymentCallbackDispatcher(
    InMemoryPaymentSimulationStore store,
    IHttpClientFactory httpClientFactory,
    IOptions<PaymentSimulatorOptions> options,
    TimeProvider timeProvider,
    ILogger<PaymentCallbackDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            IReadOnlyList<ScheduledPaymentCallback> dueCallbacks = store.LeaseDueCallbacks(timeProvider.GetUtcNow());

            foreach (ScheduledPaymentCallback callback in dueCallbacks)
            {
                await DispatchAsync(callback, stoppingToken);
            }

            int delayMilliseconds = Math.Max(10, options.Value.DispatcherPollMilliseconds);
            await Task.Delay(delayMilliseconds, stoppingToken);
        }
    }

    private async Task DispatchAsync(ScheduledPaymentCallback callback, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(callback.CallbackUrl))
        {
            store.CompleteCallback(
                callback.PaymentId,
                callback.CallbackId,
                PaymentCallbackDeliveryStatus.SkippedNoTarget,
                timeProvider.GetUtcNow(),
                "No callback URL was provided.");
            return;
        }

        try
        {
            HttpClient client = httpClientFactory.CreateClient("payment-callback-dispatcher");
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                callback.CallbackUrl,
                new
                {
                    callback.CallbackId,
                    callback.PaymentId,
                    callback.OrderId,
                    callback.ProviderReference,
                    outcome = "authorized",
                    callback.Mode,
                    callback.AmountCents,
                    callback.Currency,
                    callback.SequenceNumber,
                    dispatchedUtc = timeProvider.GetUtcNow()
                },
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                store.CompleteCallback(
                    callback.PaymentId,
                    callback.CallbackId,
                    PaymentCallbackDeliveryStatus.Delivered,
                    timeProvider.GetUtcNow(),
                    null);
            }
            else
            {
                string lastError = $"Callback target returned HTTP {(int)response.StatusCode}.";
                store.CompleteCallback(
                    callback.PaymentId,
                    callback.CallbackId,
                    PaymentCallbackDeliveryStatus.Failed,
                    timeProvider.GetUtcNow(),
                    lastError);
                logger.LogWarning(
                    "Payment callback {CallbackId} for payment {PaymentId} failed with HTTP {StatusCode}.",
                    callback.CallbackId,
                    callback.PaymentId,
                    (int)response.StatusCode);
            }
        }
        catch (Exception exception)
        {
            store.CompleteCallback(
                callback.PaymentId,
                callback.CallbackId,
                PaymentCallbackDeliveryStatus.Failed,
                timeProvider.GetUtcNow(),
                exception.GetType().Name);

            logger.LogWarning(
                exception,
                "Payment callback {CallbackId} for payment {PaymentId} failed during dispatch.",
                callback.CallbackId,
                callback.PaymentId);
        }
    }
}
