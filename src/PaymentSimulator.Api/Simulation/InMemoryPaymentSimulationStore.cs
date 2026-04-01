using System.Collections.Concurrent;

namespace PaymentSimulator.Api.Simulation;

internal sealed class InMemoryPaymentSimulationStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PaymentSimulationRecord> _records = new(StringComparer.Ordinal);

    public PaymentSimulationAttemptState BeginAttempt(
        PaymentAuthorizationRequest request,
        PaymentSimulationMode mode)
    {
        lock (_gate)
        {
            if (!_records.TryGetValue(request.PaymentId, out PaymentSimulationRecord? record))
            {
                record = new PaymentSimulationRecord(
                    request.PaymentId,
                    request.OrderId,
                    request.AmountCents,
                    request.Currency,
                    request.CallbackUrl);
                _records.Add(request.PaymentId, record);
            }

            record.OrderId = request.OrderId;
            record.AmountCents = request.AmountCents;
            record.Currency = request.Currency;
            record.CallbackUrl = NormalizeOptionalText(request.CallbackUrl);
            record.AttemptCount++;
            record.Mode = mode;

            return new PaymentSimulationAttemptState(
                request.PaymentId,
                record.AttemptCount,
                record.CallbackUrl);
        }
    }

    public void CompleteAttempt(
        string paymentId,
        PaymentSimulationMode mode,
        string outcome,
        int amountCents,
        string currency,
        string providerReference)
    {
        lock (_gate)
        {
            PaymentSimulationRecord record = GetRequiredRecord(paymentId);
            record.Mode = mode;
            record.Outcome = outcome;
            record.AmountCents = amountCents;
            record.Currency = currency;
            record.LatestProviderReference = providerReference;
        }
    }

    public void ScheduleCallbacks(
        string paymentId,
        IEnumerable<ScheduledPaymentCallback> callbacks)
    {
        lock (_gate)
        {
            PaymentSimulationRecord record = GetRequiredRecord(paymentId);

            foreach (ScheduledPaymentCallback callback in callbacks)
            {
                record.Callbacks[callback.CallbackId] = callback;
            }
        }
    }

    public IReadOnlyList<ScheduledPaymentCallback> LeaseDueCallbacks(DateTimeOffset utcNow)
    {
        lock (_gate)
        {
            List<ScheduledPaymentCallback> due = [];

            foreach (PaymentSimulationRecord record in _records.Values)
            {
                foreach (ScheduledPaymentCallback callback in record.Callbacks.Values
                             .Where(item => item.Status == PaymentCallbackDeliveryStatus.Pending && item.DueUtc <= utcNow)
                             .OrderBy(item => item.DueUtc)
                             .ThenBy(item => item.SequenceNumber))
                {
                    callback.Status = PaymentCallbackDeliveryStatus.Dispatching;
                    callback.DeliveryAttempts++;
                    due.Add(callback);
                }
            }

            return due;
        }
    }

    public void CompleteCallback(
        string paymentId,
        string callbackId,
        PaymentCallbackDeliveryStatus status,
        DateTimeOffset completedUtc,
        string? lastError)
    {
        lock (_gate)
        {
            PaymentSimulationRecord record = GetRequiredRecord(paymentId);

            if (record.Callbacks.TryGetValue(callbackId, out ScheduledPaymentCallback? callback))
            {
                callback.Status = status;
                callback.CompletedUtc = completedUtc;
                callback.LastError = NormalizeOptionalText(lastError);
            }
        }
    }

    public PaymentSimulationStatusSnapshot? GetStatus(string paymentId)
    {
        lock (_gate)
        {
            if (!_records.TryGetValue(paymentId, out PaymentSimulationRecord? record))
            {
                return null;
            }

            return new PaymentSimulationStatusSnapshot(
                record.PaymentId,
                record.OrderId,
                PaymentSimulationModeResolver.ToExternalText(record.Mode),
                record.Outcome,
                record.AttemptCount,
                record.LatestProviderReference,
                record.AmountCents,
                record.Currency,
                record.CallbackUrl,
                record.Callbacks.Values
                    .OrderBy(item => item.SequenceNumber)
                    .Select(item => item.ToSnapshot())
                    .ToArray());
        }
    }

    private PaymentSimulationRecord GetRequiredRecord(string paymentId)
    {
        if (_records.TryGetValue(paymentId, out PaymentSimulationRecord? record))
        {
            return record;
        }

        throw new InvalidOperationException($"No payment simulation record exists for '{paymentId}'.");
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class PaymentSimulationRecord(
        string paymentId,
        string orderId,
        int amountCents,
        string currency,
        string? callbackUrl)
    {
        public string PaymentId { get; } = paymentId;

        public string OrderId { get; set; } = orderId;

        public PaymentSimulationMode Mode { get; set; } = PaymentSimulationMode.FastSuccess;

        public string Outcome { get; set; } = "received";

        public int AttemptCount { get; set; }

        public string LatestProviderReference { get; set; } = string.Empty;

        public int AmountCents { get; set; } = amountCents;

        public string Currency { get; set; } = currency;

        public string? CallbackUrl { get; set; } = callbackUrl;

        public Dictionary<string, ScheduledPaymentCallback> Callbacks { get; } = new(StringComparer.Ordinal);
    }
}

internal sealed record PaymentSimulationAttemptState(
    string PaymentId,
    int AttemptNumber,
    string? CallbackUrl);

internal sealed record PaymentSimulationStatusSnapshot(
    string PaymentId,
    string OrderId,
    string Mode,
    string Outcome,
    int AttemptCount,
    string LatestProviderReference,
    int AmountCents,
    string Currency,
    string? CallbackUrl,
    IReadOnlyList<PaymentCallbackStatusSnapshot> Callbacks);

internal enum PaymentCallbackDeliveryStatus
{
    Pending,
    Dispatching,
    Delivered,
    SkippedNoTarget,
    Failed
}

internal sealed class ScheduledPaymentCallback(
    string callbackId,
    string paymentId,
    string orderId,
    string providerReference,
    string mode,
    int amountCents,
    string currency,
    string? callbackUrl,
    int sequenceNumber,
    DateTimeOffset dueUtc)
{
    public string CallbackId { get; } = callbackId;

    public string PaymentId { get; } = paymentId;

    public string OrderId { get; } = orderId;

    public string ProviderReference { get; } = providerReference;

    public string Mode { get; } = mode;

    public int AmountCents { get; } = amountCents;

    public string Currency { get; } = currency;

    public string? CallbackUrl { get; } = callbackUrl;

    public int SequenceNumber { get; } = sequenceNumber;

    public DateTimeOffset DueUtc { get; } = dueUtc;

    public PaymentCallbackDeliveryStatus Status { get; set; } = PaymentCallbackDeliveryStatus.Pending;

    public int DeliveryAttempts { get; set; }

    public DateTimeOffset? CompletedUtc { get; set; }

    public string? LastError { get; set; }

    public PaymentCallbackStatusSnapshot ToSnapshot() =>
        new(
            CallbackId,
            SequenceNumber,
            Status.ToString(),
            DueUtc,
            CompletedUtc,
            DeliveryAttempts,
            LastError);
}

internal sealed record PaymentCallbackStatusSnapshot(
    string CallbackId,
    int SequenceNumber,
    string Status,
    DateTimeOffset DueUtc,
    DateTimeOffset? CompletedUtc,
    int DeliveryAttempts,
    string? LastError);
