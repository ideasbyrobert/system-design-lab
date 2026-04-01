using System.Text.Json;
using Lab.Persistence;
using Lab.Persistence.Entities;
using Lab.Persistence.Queueing;
using Lab.Shared.Configuration;
using Lab.Shared.Queueing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Worker.Jobs;

internal sealed class PaymentConfirmationRetryJobHandler(
    PrimaryDbContext dbContext,
    IPaymentConfirmationClient paymentClient,
    IOptions<QueueOptions> queueOptions,
    TimeProvider timeProvider) : IWorkerJobHandler
{
    private const int HttpStatusOk = 200;
    private const int HttpStatusAccepted = 202;
    private const int HttpStatusNotFound = 404;
    private const int HttpStatusServiceUnavailable = 503;
    private const int HttpStatusGatewayTimeout = 504;
    private const string OrderStatusPaid = "Paid";
    private const string OrderStatusFailed = "Failed";
    private const string PaymentStatusAuthorized = "Authorized";
    private const string PaymentStatusFailed = "Failed";
    private const string PaymentStatusTimeout = "Timeout";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string JobType => LabQueueJobTypes.PaymentConfirmationRetry;

    public async Task<WorkerJobExecutionResult> HandleAsync(
        QueueJobRecord job,
        WorkerJobTraceBuilder trace,
        CancellationToken cancellationToken)
    {
        PaymentConfirmationRetryJobPayload payload;

        try
        {
            payload = JsonSerializer.Deserialize<PaymentConfirmationRetryJobPayload>(job.PayloadJson, JsonOptions)
                ?? throw new InvalidOperationException("Queue job payload was empty.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            trace.SetErrorCode("invalid_payment_job_payload");
            trace.AddNote("Payment confirmation retry job payload could not be parsed.");
            return new WorkerJobExecutionResult(
                Disposition: WorkerJobDisposition.Failed,
                ContractSatisfied: false,
                RunId: $"worker-job-{job.QueueJobId}",
                ErrorCode: "invalid_payment_job_payload",
                ErrorDetail: exception.Message);
        }

        string runId = string.IsNullOrWhiteSpace(payload.RunId) ? $"worker-job-{job.QueueJobId}" : payload.RunId.Trim();

        using (WorkerJobTraceBuilder.StageScope stage = trace.BeginStage(
                   "payment_record_loaded",
                   new Dictionary<string, string?>
                   {
                       ["paymentId"] = payload.PaymentId,
                       ["orderId"] = payload.OrderId
                   }))
        {
            Payment? payment = await dbContext.Payments
                .Include(item => item.Order)
                .SingleOrDefaultAsync(item => item.PaymentId == payload.PaymentId && item.OrderId == payload.OrderId, cancellationToken);

            if (payment is null)
            {
                stage.Complete("missing");
                trace.SetErrorCode("payment_record_not_found");
                return new WorkerJobExecutionResult(
                    Disposition: WorkerJobDisposition.Failed,
                    ContractSatisfied: false,
                    RunId: runId,
                    ErrorCode: "payment_record_not_found",
                    ErrorDetail: $"No payment record exists for payment '{payload.PaymentId}' and order '{payload.OrderId}'.");
            }

            stage.Complete(
                "loaded",
                new Dictionary<string, string?>
                {
                    ["paymentStatus"] = payment.Status,
                    ["orderStatus"] = payment.Order.Status,
                    ["statusCheckOnly"] = payload.StatusCheckOnly.ToString().ToLowerInvariant(),
                    ["retryCount"] = job.RetryCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });

            if (IsAlreadyResolved(payment))
            {
                trace.AddNote("Payment confirmation retry job found the payment already in a terminal state and finished without additional provider work.");
                trace.RecordInstantStage(
                    "payment_confirmation_skipped",
                    outcome: "already_resolved",
                    metadata: new Dictionary<string, string?>
                    {
                        ["paymentStatus"] = payment.Status,
                        ["orderStatus"] = payment.Order.Status
                    });

                return new WorkerJobExecutionResult(
                    Disposition: WorkerJobDisposition.Completed,
                    ContractSatisfied: true,
                    RunId: runId);
            }

            PaymentProviderObservation observation = payload.StatusCheckOnly
                ? await PollStatusAsync(payment, runId, trace, cancellationToken)
                : await AuthorizeAsync(payment, runId, trace, cancellationToken);

            return await ApplyObservationAsync(job, payment, payload, observation, runId, trace, cancellationToken);
        }
    }

    private async Task<PaymentProviderObservation> AuthorizeAsync(
        Payment payment,
        string runId,
        WorkerJobTraceBuilder trace,
        CancellationToken cancellationToken)
    {
        using WorkerJobTraceBuilder.StageScope stage = trace.BeginStage(
            "payment_authorization_requested",
            new Dictionary<string, string?>
            {
                ["paymentId"] = payment.PaymentId,
                ["orderId"] = payment.OrderId,
                ["paymentMode"] = payment.Mode
            });

        using WorkerJobTraceBuilder.DependencyScope dependency = trace.BeginDependencyCall(
            dependencyName: "payment-simulator",
            route: "/payments/authorize",
            region: "local",
            metadata: new Dictionary<string, string?>
            {
                ["paymentId"] = payment.PaymentId,
                ["orderId"] = payment.OrderId,
                ["paymentMode"] = payment.Mode
            });

        PaymentProviderObservation observation = await paymentClient.AuthorizeAsync(
            new PaymentAuthorizationCommand(
                PaymentId: payment.PaymentId,
                OrderId: payment.OrderId,
                AmountCents: payment.AmountCents,
                Currency: "USD",
                PaymentMode: payment.Mode ?? "fast_success"),
            runId,
            $"job-{payment.PaymentId}",
            cancellationToken);

        dependency.Complete(
            statusCode: observation.StatusCode,
            outcome: observation.StatusCode < 400 ? "success" : "failed",
            metadataOverride: BuildDependencyMetadata(observation),
            notesOverride: BuildDependencyNotes(observation));

        stage.Complete(
            observation.StatusCode < 400 ? observation.Outcome : observation.ErrorCode ?? "failed",
            new Dictionary<string, string?>
            {
                ["statusCode"] = observation.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["providerReference"] = observation.ProviderReference,
                ["callbackPending"] = observation.CallbackPending.ToString().ToLowerInvariant(),
                ["callbackCountScheduled"] = observation.CallbackCountScheduled.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });

        return observation;
    }

    private async Task<PaymentProviderObservation> PollStatusAsync(
        Payment payment,
        string runId,
        WorkerJobTraceBuilder trace,
        CancellationToken cancellationToken)
    {
        using WorkerJobTraceBuilder.StageScope stage = trace.BeginStage(
            "payment_status_checked",
            new Dictionary<string, string?>
            {
                ["paymentId"] = payment.PaymentId,
                ["orderId"] = payment.OrderId
            });

        using WorkerJobTraceBuilder.DependencyScope dependency = trace.BeginDependencyCall(
            dependencyName: "payment-simulator",
            route: $"/payments/authorizations/{payment.PaymentId}",
            region: "local",
            metadata: new Dictionary<string, string?>
            {
                ["paymentId"] = payment.PaymentId,
                ["orderId"] = payment.OrderId
            });

        PaymentProviderObservation observation = await paymentClient.GetStatusAsync(
            payment.PaymentId,
            runId,
            $"job-{payment.PaymentId}",
            cancellationToken);

        dependency.Complete(
            statusCode: observation.StatusCode,
            outcome: observation.StatusCode < 400 ? "success" : "failed",
            metadataOverride: BuildDependencyMetadata(observation),
            notesOverride: BuildDependencyNotes(observation));

        stage.Complete(
            observation.StatusCode < 400 ? observation.Outcome : observation.ErrorCode ?? "failed",
            new Dictionary<string, string?>
            {
                ["statusCode"] = observation.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["providerReference"] = observation.ProviderReference,
                ["callbackPending"] = observation.CallbackPending.ToString().ToLowerInvariant(),
                ["callbackCountScheduled"] = observation.CallbackCountScheduled.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });

        return observation;
    }

    private async Task<WorkerJobExecutionResult> ApplyObservationAsync(
        QueueJobRecord job,
        Payment payment,
        PaymentConfirmationRetryJobPayload payload,
        PaymentProviderObservation observation,
        string runId,
        WorkerJobTraceBuilder trace,
        CancellationToken cancellationToken)
    {
        DateTimeOffset nowUtc = timeProvider.GetUtcNow();

        if (observation.StatusCode == HttpStatusOk &&
            string.Equals(observation.Outcome, "authorized", StringComparison.Ordinal))
        {
            using WorkerJobTraceBuilder.StageScope stage = trace.BeginStage(
                "payment_state_persisted",
                new Dictionary<string, string?>
                {
                    ["paymentId"] = payment.PaymentId,
                    ["orderId"] = payment.OrderId
                });

            payment.Status = PaymentStatusAuthorized;
            payment.ExternalReference = observation.ProviderReference;
            payment.ErrorCode = null;
            payment.AttemptedUtc = nowUtc;
            payment.ConfirmedUtc = nowUtc;
            payment.Order.Status = OrderStatusPaid;
            dbContext.QueueJobs.Add(CreateOrderHistoryProjectionQueueJob(payment.Order, runId, nowUtc));
            await dbContext.SaveChangesAsync(cancellationToken);

            stage.Complete(
                "paid",
                new Dictionary<string, string?>
                {
                    ["paymentStatus"] = payment.Status,
                    ["orderStatus"] = payment.Order.Status
                });

            trace.AddNote("Payment confirmation retry worker marked the order as paid after provider authorization succeeded.");
            return new WorkerJobExecutionResult(WorkerJobDisposition.Completed, true, runId);
        }

        if ((observation.StatusCode == HttpStatusAccepted || string.Equals(observation.Outcome, "pending_confirmation", StringComparison.Ordinal)) &&
            CanRetry(job))
        {
            DateTimeOffset nextAttemptUtc = nowUtc.AddMilliseconds(GetRetryDelayMilliseconds(job));
            string updatedPayloadJson = SerializePayload(payload with { StatusCheckOnly = true });
            trace.RecordInstantStage(
                "payment_retry_scheduled",
                outcome: "pending_confirmation",
                metadata: new Dictionary<string, string?>
                {
                    ["nextAttemptUtc"] = nextAttemptUtc.ToString("O"),
                    ["statusCheckOnly"] = "true"
                });

            trace.AddNote("Payment confirmation remained pending, so the worker rescheduled the job for a later status check.");
            return new WorkerJobExecutionResult(
                WorkerJobDisposition.Rescheduled,
                false,
                runId,
                ErrorCode: "payment_pending_confirmation",
                ErrorDetail: "Payment confirmation is still pending.",
                NextAttemptUtc: nextAttemptUtc,
                UpdatedPayloadJson: updatedPayloadJson);
        }

        if (ShouldRetryAuthorization(observation) && CanRetry(job))
        {
            DateTimeOffset nextAttemptUtc = nowUtc.AddMilliseconds(GetRetryDelayMilliseconds(job));
            string updatedPayloadJson = SerializePayload(payload);
            trace.RecordInstantStage(
                "payment_retry_scheduled",
                outcome: "retry_authorization",
                metadata: new Dictionary<string, string?>
                {
                    ["nextAttemptUtc"] = nextAttemptUtc.ToString("O"),
                    ["statusCheckOnly"] = payload.StatusCheckOnly.ToString().ToLowerInvariant()
                });

            trace.AddNote("Payment authorization reported a retriable outcome, so the worker rescheduled the job.");
            return new WorkerJobExecutionResult(
                WorkerJobDisposition.Rescheduled,
                false,
                runId,
                ErrorCode: observation.ErrorCode ?? "payment_retry_scheduled",
                ErrorDetail: observation.ErrorDetail,
                NextAttemptUtc: nextAttemptUtc,
                UpdatedPayloadJson: updatedPayloadJson);
        }

        using (WorkerJobTraceBuilder.StageScope stage = trace.BeginStage(
                   "payment_state_persisted",
                   new Dictionary<string, string?>
                   {
                       ["paymentId"] = payment.PaymentId,
                       ["orderId"] = payment.OrderId
                   }))
        {
            payment.Status = DeriveFailedPaymentStatus(observation);
            payment.ExternalReference = observation.ProviderReference ?? payment.ExternalReference;
            payment.ErrorCode = DeriveErrorCode(observation);
            payment.AttemptedUtc = nowUtc;
            payment.ConfirmedUtc = null;
            payment.Order.Status = OrderStatusFailed;
            dbContext.QueueJobs.Add(CreateOrderHistoryProjectionQueueJob(payment.Order, runId, nowUtc));
            await dbContext.SaveChangesAsync(cancellationToken);

            stage.Complete(
                "failed",
                new Dictionary<string, string?>
                {
                    ["paymentStatus"] = payment.Status,
                    ["orderStatus"] = payment.Order.Status,
                    ["paymentErrorCode"] = payment.ErrorCode
                });
        }

        trace.SetErrorCode(DeriveErrorCode(observation) ?? "payment_confirmation_failed");
        trace.AddNote("Payment confirmation retry worker marked the order as failed because the provider did not reach an authorized state.");

        return new WorkerJobExecutionResult(
            WorkerJobDisposition.Failed,
            false,
            runId,
            ErrorCode: payment.ErrorCode,
            ErrorDetail: observation.ErrorDetail);
    }

    private bool CanRetry(QueueJobRecord job) => job.RetryCount < queueOptions.Value.MaxRetryAttempts;

    private int GetRetryDelayMilliseconds(QueueJobRecord job)
    {
        int baseDelay = Math.Max(queueOptions.Value.PollIntervalMilliseconds, 250);
        int multiplier = Math.Clamp(job.RetryCount + 1, 1, 4);
        return baseDelay * multiplier;
    }

    private static bool IsAlreadyResolved(Payment payment) =>
        string.Equals(payment.Status, PaymentStatusAuthorized, StringComparison.Ordinal) ||
        string.Equals(payment.Status, PaymentStatusFailed, StringComparison.Ordinal) ||
        string.Equals(payment.Status, PaymentStatusTimeout, StringComparison.Ordinal) ||
        string.Equals(payment.Order.Status, OrderStatusPaid, StringComparison.Ordinal) ||
        string.Equals(payment.Order.Status, OrderStatusFailed, StringComparison.Ordinal);

    private static bool ShouldRetryAuthorization(PaymentProviderObservation observation) =>
        observation.StatusCode == HttpStatusServiceUnavailable ||
        string.Equals(observation.Outcome, "transient_failure", StringComparison.Ordinal) ||
        string.Equals(observation.ErrorCode, "simulated_transient_failure", StringComparison.Ordinal) ||
        observation.StatusCode == HttpStatusNotFound;

    private static string DeriveFailedPaymentStatus(PaymentProviderObservation observation) =>
        observation.StatusCode == HttpStatusGatewayTimeout ||
        string.Equals(observation.Outcome, "timeout", StringComparison.Ordinal) ||
        string.Equals(observation.ErrorCode, "simulated_timeout", StringComparison.Ordinal)
            ? PaymentStatusTimeout
            : PaymentStatusFailed;

    private static QueueJob CreateOrderHistoryProjectionQueueJob(
        Order order,
        string runId,
        DateTimeOffset enqueuedUtc) =>
        new()
        {
            QueueJobId = $"job-{Guid.NewGuid():N}",
            JobType = LabQueueJobTypes.OrderHistoryProjectionUpdate,
            PayloadJson = JsonSerializer.Serialize(
                new OrderHistoryProjectionUpdateJobPayload(order.OrderId, order.UserId, runId),
                JsonOptions),
            Status = QueueJobStatuses.Pending,
            AvailableUtc = enqueuedUtc,
            EnqueuedUtc = enqueuedUtc,
            RetryCount = 0
        };

    private static string SerializePayload(PaymentConfirmationRetryJobPayload payload) =>
        JsonSerializer.Serialize(payload, JsonOptions);

    private static string? DeriveErrorCode(PaymentProviderObservation observation) =>
        observation.ErrorCode ??
        (string.Equals(observation.Outcome, "timeout", StringComparison.Ordinal) ? "simulated_timeout" : null);

    private static IReadOnlyDictionary<string, string?> BuildDependencyMetadata(PaymentProviderObservation observation) =>
        new Dictionary<string, string?>
        {
            ["paymentOutcome"] = observation.Outcome,
            ["providerReference"] = observation.ProviderReference,
            ["paymentErrorCode"] = observation.ErrorCode,
            ["callbackPending"] = observation.CallbackPending.ToString().ToLowerInvariant(),
            ["callbackCountScheduled"] = observation.CallbackCountScheduled.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["downstreamRunId"] = observation.DownstreamRunId,
            ["downstreamTraceId"] = observation.DownstreamTraceId,
            ["downstreamRequestId"] = observation.DownstreamRequestId
        };

    private static IReadOnlyList<string> BuildDependencyNotes(PaymentProviderObservation observation) =>
        observation.ErrorDetail is null
            ? Array.Empty<string>()
            : [observation.ErrorDetail];
}
