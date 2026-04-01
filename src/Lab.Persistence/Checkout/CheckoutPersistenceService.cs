using Lab.Persistence.Entities;
using Lab.Persistence.Queueing;
using Microsoft.EntityFrameworkCore;

namespace Lab.Persistence.Checkout;

public sealed class CheckoutPersistenceService(PrimaryDbContext dbContext)
{
    public async Task<InventoryReservationResult> ReserveInventoryAsync(
        IReadOnlyList<InventoryReservationRequest> reservations,
        DateTimeOffset updatedUtc,
        CancellationToken cancellationToken = default)
    {
        InventoryReservationResult validationFailure = ValidateReservations(reservations);

        if (!validationFailure.Succeeded)
        {
            return validationFailure;
        }

        IReadOnlyList<InventoryReservationRequest> normalizedReservations = NormalizeReservations(reservations);

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        InventoryReservationResult reservationResult = await ReserveInventoryInternalAsync(
            normalizedReservations,
            updatedUtc,
            cancellationToken);

        if (!reservationResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return reservationResult;
        }

        await transaction.CommitAsync(cancellationToken);
        return reservationResult;
    }

    public async Task<CheckoutPersistenceResult> ReserveInventoryAndPersistOrderAsync(
        CheckoutPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        CheckoutPersistenceFailure? requestFailure = ValidateCheckoutRequest(request);

        if (requestFailure is not null)
        {
            return CheckoutPersistenceResult.Fail(
                requestFailure.Code,
                requestFailure.Detail,
                requestFailure.ProductId);
        }

        IReadOnlyList<InventoryReservationRequest> reservations = NormalizeReservations(
            request.Items
                .Select(item => new InventoryReservationRequest(item.ProductId, item.Quantity))
                .ToArray());

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        InventoryReservationResult reservationResult = await ReserveInventoryInternalAsync(
            reservations,
            request.CreatedUtc,
            cancellationToken);

        if (!reservationResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CheckoutPersistenceResult.Fail(
                reservationResult.Failure!.Code,
                reservationResult.Failure.Detail,
                reservationResult.Failure.ProductId);
        }

        int totalPriceCents = CalculateTotalPriceCents(request.Items);

        Order order = new()
        {
            OrderId = request.OrderId.Trim(),
            UserId = request.UserId.Trim(),
            CartId = NormalizeOptionalText(request.CartId),
            Region = request.Region.Trim(),
            Status = request.OrderStatus.Trim(),
            TotalPriceCents = totalPriceCents,
            CreatedUtc = request.CreatedUtc,
            SubmittedUtc = request.SubmittedUtc
        };

        foreach (CheckoutOrderItemPersistenceRequest item in request.Items)
        {
            order.Items.Add(new OrderItem
            {
                OrderItemId = $"oi-{Guid.NewGuid():N}",
                OrderId = order.OrderId,
                ProductId = item.ProductId.Trim(),
                Quantity = item.Quantity,
                UnitPriceCents = item.UnitPriceCents
            });
        }

        if (request.Payment is not null)
        {
            order.Payments.Add(new Payment
            {
                PaymentId = request.Payment.PaymentId.Trim(),
                OrderId = order.OrderId,
                Provider = request.Payment.Provider.Trim(),
                IdempotencyKey = request.Payment.IdempotencyKey.Trim(),
                Mode = request.Payment.Mode.Trim(),
                Status = request.Payment.Status.Trim(),
                AmountCents = request.Payment.AmountCents,
                ExternalReference = NormalizeOptionalText(request.Payment.ExternalReference),
                ErrorCode = NormalizeOptionalText(request.Payment.ErrorCode),
                AttemptedUtc = request.Payment.AttemptedUtc,
                ConfirmedUtc = request.Payment.ConfirmedUtc
            });
        }

        await dbContext.Orders.AddAsync(order, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CheckoutPersistenceResult.Success(
            order.OrderId,
            request.Payment?.PaymentId.Trim(),
            totalPriceCents,
            reservationResult.Inventory);
    }

    public async Task<CheckoutPersistenceResult> ReserveInventoryPersistOrderAndEnqueueJobAsync(
        CheckoutPersistenceRequest request,
        EnqueueQueueJobRequest queueJobRequest,
        CancellationToken cancellationToken = default)
    {
        return await ReserveInventoryPersistOrderAndEnqueueJobsAsync(
            request,
            [queueJobRequest],
            cancellationToken);
    }

    public async Task<CheckoutPersistenceResult> ReserveInventoryPersistOrderAndEnqueueJobsAsync(
        CheckoutPersistenceRequest request,
        IReadOnlyList<EnqueueQueueJobRequest> queueJobRequests,
        CancellationToken cancellationToken = default)
    {
        CheckoutPersistenceFailure? requestFailure = ValidateCheckoutRequest(request);

        if (requestFailure is not null)
        {
            return CheckoutPersistenceResult.Fail(
                requestFailure.Code,
                requestFailure.Detail,
                requestFailure.ProductId);
        }

        if (queueJobRequests.Count == 0)
        {
            throw new ArgumentException("At least one queue job must be supplied.", nameof(queueJobRequests));
        }

        foreach (EnqueueQueueJobRequest queueJobRequest in queueJobRequests)
        {
            ValidateQueueJobRequest(queueJobRequest);
        }

        IReadOnlyList<InventoryReservationRequest> reservations = NormalizeReservations(
            request.Items
                .Select(item => new InventoryReservationRequest(item.ProductId, item.Quantity))
                .ToArray());

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        InventoryReservationResult reservationResult = await ReserveInventoryInternalAsync(
            reservations,
            request.CreatedUtc,
            cancellationToken);

        if (!reservationResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CheckoutPersistenceResult.Fail(
                reservationResult.Failure!.Code,
                reservationResult.Failure.Detail,
                reservationResult.Failure.ProductId);
        }

        int totalPriceCents = CalculateTotalPriceCents(request.Items);
        Order order = CreateOrderEntity(request, totalPriceCents);

        dbContext.Orders.Add(order);
        dbContext.QueueJobs.AddRange(queueJobRequests.Select(queueJobRequest => new QueueJob
        {
            QueueJobId = queueJobRequest.QueueJobId.Trim(),
            JobType = queueJobRequest.JobType.Trim(),
            PayloadJson = queueJobRequest.PayloadJson,
            Status = QueueJobStatuses.Pending,
            AvailableUtc = queueJobRequest.AvailableUtc ?? queueJobRequest.EnqueuedUtc,
            EnqueuedUtc = queueJobRequest.EnqueuedUtc,
            RetryCount = 0
        }));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CheckoutPersistenceResult.Success(
            order.OrderId,
            request.Payment?.PaymentId.Trim(),
            totalPriceCents,
            reservationResult.Inventory,
            queueJobRequests[0].QueueJobId.Trim());
    }

    private async Task<InventoryReservationResult> ReserveInventoryInternalAsync(
        IReadOnlyList<InventoryReservationRequest> reservations,
        DateTimeOffset updatedUtc,
        CancellationToken cancellationToken)
    {
        List<InventoryReservationSnapshot> snapshots = new(reservations.Count);

        foreach (InventoryReservationRequest reservation in reservations.OrderBy(item => item.ProductId, StringComparer.Ordinal))
        {
            int affectedRows = await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE inventory
                SET available_quantity = available_quantity - {reservation.Quantity},
                    reserved_quantity = reserved_quantity + {reservation.Quantity},
                    version = version + 1,
                    updated_utc = {updatedUtc}
                WHERE product_id = {reservation.ProductId}
                  AND available_quantity >= {reservation.Quantity};
                """,
                cancellationToken);

            if (affectedRows != 1)
            {
                CheckoutPersistenceFailure failure = await BuildInventoryFailureAsync(reservation, cancellationToken);
                return InventoryReservationResult.Fail(failure.Code, failure.Detail, failure.ProductId);
            }

            InventoryReservationSnapshot snapshot = await dbContext.Inventory
                .AsNoTracking()
                .Where(item => item.ProductId == reservation.ProductId)
                .Select(item => new InventoryReservationSnapshot(
                    item.ProductId,
                    item.AvailableQuantity,
                    item.ReservedQuantity,
                    item.Version,
                    item.UpdatedUtc))
                .SingleAsync(cancellationToken);

            snapshots.Add(snapshot);
        }

        return InventoryReservationResult.Success(snapshots);
    }

    private async Task<CheckoutPersistenceFailure> BuildInventoryFailureAsync(
        InventoryReservationRequest reservation,
        CancellationToken cancellationToken)
    {
        InventoryRecord? inventory = await dbContext.Inventory
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.ProductId == reservation.ProductId, cancellationToken);

        if (inventory is null)
        {
            return new CheckoutPersistenceFailure(
                Code: "inventory_not_found",
                Detail: $"Inventory record '{reservation.ProductId}' does not exist.",
                ProductId: reservation.ProductId);
        }

        return new CheckoutPersistenceFailure(
            Code: "insufficient_inventory",
            Detail:
                $"Inventory record '{reservation.ProductId}' has only {inventory.AvailableQuantity} available units, which is less than requested quantity {reservation.Quantity}.",
            ProductId: reservation.ProductId);
    }

    private static InventoryReservationResult ValidateReservations(IReadOnlyList<InventoryReservationRequest> reservations)
    {
        if (reservations.Count == 0)
        {
            return InventoryReservationResult.Fail(
                "invalid_inventory_reservation",
                "At least one inventory reservation is required.");
        }

        foreach (InventoryReservationRequest reservation in reservations)
        {
            if (string.IsNullOrWhiteSpace(reservation.ProductId))
            {
                return InventoryReservationResult.Fail(
                    "invalid_inventory_product_id",
                    "Inventory reservation requires a non-empty product identifier.");
            }

            if (reservation.Quantity <= 0)
            {
                return InventoryReservationResult.Fail(
                    "invalid_inventory_quantity",
                    "Inventory reservation quantity must be greater than zero.",
                    reservation.ProductId);
            }
        }

        return InventoryReservationResult.Success([]);
    }

    private static CheckoutPersistenceFailure? ValidateCheckoutRequest(CheckoutPersistenceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OrderId))
        {
            return new CheckoutPersistenceFailure("invalid_order_id", "A non-empty order identifier is required.", null);
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return new CheckoutPersistenceFailure("invalid_user_id", "A non-empty user identifier is required.", null);
        }

        if (string.IsNullOrWhiteSpace(request.Region))
        {
            return new CheckoutPersistenceFailure("invalid_region", "A non-empty region is required.", null);
        }

        if (string.IsNullOrWhiteSpace(request.OrderStatus))
        {
            return new CheckoutPersistenceFailure("invalid_order_status", "A non-empty order status is required.", null);
        }

        InventoryReservationResult reservationValidation = ValidateReservations(
            request.Items
                .Select(item => new InventoryReservationRequest(item.ProductId, item.Quantity))
                .ToArray());

        if (!reservationValidation.Succeeded)
        {
            return reservationValidation.Failure;
        }

        foreach (CheckoutOrderItemPersistenceRequest item in request.Items)
        {
            if (item.UnitPriceCents < 0)
            {
                return new CheckoutPersistenceFailure(
                    "invalid_unit_price",
                    "Order-item unit price cannot be negative.",
                    item.ProductId);
            }
        }

        int totalPriceCents = CalculateTotalPriceCents(request.Items);

        if (request.Payment is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.Payment.IdempotencyKey))
        {
            return new CheckoutPersistenceFailure("invalid_idempotency_key", "A non-empty idempotency key is required.", null);
        }

        if (string.IsNullOrWhiteSpace(request.Payment.Mode))
        {
            return new CheckoutPersistenceFailure("invalid_payment_mode", "A non-empty payment mode is required.", null);
        }

        if (string.IsNullOrWhiteSpace(request.Payment.PaymentId))
        {
            return new CheckoutPersistenceFailure("invalid_payment_id", "A non-empty payment identifier is required.", null);
        }

        if (string.IsNullOrWhiteSpace(request.Payment.Provider))
        {
            return new CheckoutPersistenceFailure("invalid_payment_provider", "A non-empty payment provider is required.", null);
        }

        if (string.IsNullOrWhiteSpace(request.Payment.Status))
        {
            return new CheckoutPersistenceFailure("invalid_payment_status", "A non-empty payment status is required.", null);
        }

        if (request.Payment.AmountCents != totalPriceCents)
        {
            return new CheckoutPersistenceFailure(
                "payment_amount_mismatch",
                $"Payment amount {request.Payment.AmountCents} does not match order total {totalPriceCents}.",
                null);
        }

        return null;
    }

    private static void ValidateQueueJobRequest(EnqueueQueueJobRequest queueJobRequest)
    {
        if (string.IsNullOrWhiteSpace(queueJobRequest.QueueJobId))
        {
            throw new ArgumentException("A non-empty queue job identifier is required.", nameof(queueJobRequest));
        }

        if (string.IsNullOrWhiteSpace(queueJobRequest.JobType))
        {
            throw new ArgumentException("A non-empty queue job type is required.", nameof(queueJobRequest));
        }

        if (string.IsNullOrWhiteSpace(queueJobRequest.PayloadJson))
        {
            throw new ArgumentException("A non-empty queue job payload is required.", nameof(queueJobRequest));
        }
    }

    private static Order CreateOrderEntity(CheckoutPersistenceRequest request, int totalPriceCents)
    {
        Order order = new()
        {
            OrderId = request.OrderId.Trim(),
            UserId = request.UserId.Trim(),
            CartId = NormalizeOptionalText(request.CartId),
            Region = request.Region.Trim(),
            Status = request.OrderStatus.Trim(),
            TotalPriceCents = totalPriceCents,
            CreatedUtc = request.CreatedUtc,
            SubmittedUtc = request.SubmittedUtc
        };

        foreach (CheckoutOrderItemPersistenceRequest item in request.Items)
        {
            order.Items.Add(new OrderItem
            {
                OrderItemId = $"oi-{Guid.NewGuid():N}",
                OrderId = order.OrderId,
                ProductId = item.ProductId.Trim(),
                Quantity = item.Quantity,
                UnitPriceCents = item.UnitPriceCents
            });
        }

        if (request.Payment is not null)
        {
            order.Payments.Add(new Payment
            {
                PaymentId = request.Payment.PaymentId.Trim(),
                OrderId = order.OrderId,
                Provider = request.Payment.Provider.Trim(),
                IdempotencyKey = request.Payment.IdempotencyKey.Trim(),
                Mode = request.Payment.Mode.Trim(),
                Status = request.Payment.Status.Trim(),
                AmountCents = request.Payment.AmountCents,
                ExternalReference = NormalizeOptionalText(request.Payment.ExternalReference),
                ErrorCode = NormalizeOptionalText(request.Payment.ErrorCode),
                AttemptedUtc = request.Payment.AttemptedUtc,
                ConfirmedUtc = request.Payment.ConfirmedUtc
            });
        }

        return order;
    }

    private static IReadOnlyList<InventoryReservationRequest> NormalizeReservations(
        IReadOnlyList<InventoryReservationRequest> reservations) =>
        reservations
            .GroupBy(item => item.ProductId.Trim(), StringComparer.Ordinal)
            .Select(group => new InventoryReservationRequest(
                group.Key,
                checked(group.Sum(item => item.Quantity))))
            .ToArray();

    private static int CalculateTotalPriceCents(IReadOnlyList<CheckoutOrderItemPersistenceRequest> items)
    {
        long total = 0;

        foreach (CheckoutOrderItemPersistenceRequest item in items)
        {
            total += (long)item.Quantity * item.UnitPriceCents;
        }

        return checked((int)total);
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
