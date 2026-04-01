using System.Text.Json;
using Lab.Persistence.Entities;

namespace Lab.Persistence.Projections;

public static class OrderHistoryProjectionMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ReadModelOrderHistory CreateProjectionRow(Order order, DateTimeOffset projectedUtc)
    {
        ArgumentNullException.ThrowIfNull(order);

        OrderHistoryProjectionSummary summary = CreateSummary(order);

        return new ReadModelOrderHistory
        {
            OrderId = order.OrderId,
            UserId = order.UserId,
            Region = order.Region,
            Status = order.Status,
            OrderCreatedUtc = order.CreatedUtc,
            ProjectionVersion = summary.Versions.ProjectionVersion,
            SummaryJson = JsonSerializer.Serialize(summary, JsonOptions),
            ProjectedUtc = projectedUtc
        };
    }

    public static OrderHistoryProjectionSummary CreateSummary(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        Payment? payment = order.Payments
            .OrderByDescending(item => item.AttemptedUtc)
            .ThenByDescending(item => item.PaymentId)
            .FirstOrDefault();

        OrderItem[] items = order.Items
            .OrderBy(item => item.ProductId, StringComparer.Ordinal)
            .ToArray();

        long projectionVersion = CalculateProjectionVersion(order, payment);

        return new OrderHistoryProjectionSummary(
            OrderId: order.OrderId,
            UserId: order.UserId,
            Region: order.Region,
            Status: order.Status,
            TotalAmountCents: order.TotalPriceCents,
            ItemCount: items.Sum(item => item.Quantity),
            CreatedUtc: order.CreatedUtc,
            SubmittedUtc: order.SubmittedUtc,
            Payment: payment is null
                ? null
                : new OrderHistoryProjectionPaymentSummary(
                    PaymentId: payment.PaymentId,
                    Provider: payment.Provider,
                    Status: payment.Status,
                    Mode: NormalizeOptionalText(payment.Mode),
                    AmountCents: payment.AmountCents,
                    ProviderReference: NormalizeOptionalText(payment.ExternalReference),
                    ErrorCode: NormalizeOptionalText(payment.ErrorCode),
                    AttemptedUtc: payment.AttemptedUtc,
                    ConfirmedUtc: payment.ConfirmedUtc),
            Items: items
                .Select(item => new OrderHistoryProjectionItemSummary(
                    ProductId: item.ProductId,
                    ProductName: item.Product?.Name ?? item.ProductId,
                    Quantity: item.Quantity,
                    UnitPriceCents: item.UnitPriceCents,
                    LineSubtotalCents: checked(item.Quantity * item.UnitPriceCents)))
                .ToArray(),
            Versions: new OrderHistoryProjectionSourceVersions(
                ProjectionVersion: projectionVersion));
    }

    private static long CalculateProjectionVersion(Order order, Payment? payment)
    {
        long version = order.CreatedUtc.UtcTicks;

        if (order.SubmittedUtc is not null)
        {
            version = Math.Max(version, order.SubmittedUtc.Value.UtcTicks);
        }

        if (payment is not null)
        {
            version = Math.Max(version, payment.AttemptedUtc.UtcTicks);

            if (payment.ConfirmedUtc is not null)
            {
                version = Math.Max(version, payment.ConfirmedUtc.Value.UtcTicks);
            }
        }

        return version;
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
