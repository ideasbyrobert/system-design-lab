using System.Text.Json;
using Lab.Persistence;
using Lab.Persistence.Entities;
using Lab.Persistence.Projections;
using Lab.Shared.Configuration;
using Lab.Shared.RegionalReads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Storefront.Api.ProductPages;

namespace Storefront.Api.OrderHistory;

internal sealed class StorefrontOrderHistoryReadService(
    EnvironmentLayout layout,
    IOptions<RegionOptions> regionOptionsAccessor,
    PrimaryDatabaseInitializer primaryDatabaseInitializer,
    PrimaryDbContextFactory primaryDbContextFactory,
    ReadModelDatabaseInitializer readModelDatabaseInitializer,
    ReadModelDbContextFactory readModelDbContextFactory,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<StorefrontOrderHistoryReadResult> GetByUserIdAsync(
        string userId,
        OrderHistoryReadSource readSource,
        CancellationToken cancellationToken)
    {
        string normalizedUserId = userId.Trim();
        RegionOptions regionOptions = regionOptionsAccessor.Value;
        RegionalReadPreference initialPreference = RegionalOrderHistoryReadPreferenceResolver.Resolve(
            layout.CurrentRegion,
            regionOptions,
            readSource.ToText());

        return initialPreference.EffectiveReadSource switch
        {
            "read-model" => await GetReadModelPreferredResultAsync(normalizedUserId, readSource, initialPreference, regionOptions, cancellationToken),
            "primary-projection" => await GetFromPrimaryProjectionAsync(normalizedUserId, initialPreference, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(readSource), readSource, "Unknown order-history read source.")
        };
    }

    private async Task<StorefrontOrderHistoryReadResult> GetReadModelPreferredResultAsync(
        string normalizedUserId,
        OrderHistoryReadSource requestedReadSource,
        RegionalReadPreference initialPreference,
        RegionOptions regionOptions,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GetFromReadModelAsync(normalizedUserId, initialPreference, cancellationToken);
        }
        catch (InvalidDataException) when (requestedReadSource == OrderHistoryReadSource.Local)
        {
            RegionalReadPreference fallbackPreference = RegionalOrderHistoryReadPreferenceResolver.CreatePrimaryProjectionFallback(
                layout.CurrentRegion,
                regionOptions,
                requestedReadSource.ToText(),
                "read_model_invalid");

            return await GetFromPrimaryProjectionAsync(normalizedUserId, fallbackPreference, cancellationToken);
        }
    }

    private async Task<StorefrontOrderHistoryReadResult> GetFromReadModelAsync(
        string normalizedUserId,
        RegionalReadPreference readPreference,
        CancellationToken cancellationToken)
    {
        await readModelDatabaseInitializer.InitializeAsync(layout.ReadModelDatabasePath, cancellationToken);

        await using ReadModelDbContext dbContext = readModelDbContextFactory.CreateDbContext(layout.ReadModelDatabasePath);
        List<ReadModelOrderHistory> rows = await dbContext.OrderHistories
            .AsNoTracking()
            .Where(row => row.UserId == normalizedUserId)
            .ToListAsync(cancellationToken);

        StorefrontOrderHistorySnapshot[] observedSnapshots = rows
            .OrderByDescending(row => row.OrderCreatedUtc)
            .ThenByDescending(row => row.OrderId, StringComparer.Ordinal)
            .Select(MapRow)
            .ToArray();

        DateTimeOffset comparisonUtc = timeProvider.GetUtcNow();
        StorefrontOrderHistorySnapshot[] primarySnapshots = await LoadPrimaryProjectionSnapshotsAsync(
            normalizedUserId,
            comparisonUtc,
            cancellationToken);

        return new StorefrontOrderHistoryReadResult(
            Orders: observedSnapshots,
            Freshness: CreateFreshnessInfo(
                OrderHistoryReadSource.ReadModel.ToText(),
                observedSnapshots,
                primarySnapshots,
                comparisonUtc),
            ReadPreference: readPreference);
    }

    private async Task<StorefrontOrderHistoryReadResult> GetFromPrimaryProjectionAsync(
        string normalizedUserId,
        RegionalReadPreference readPreference,
        CancellationToken cancellationToken)
    {
        DateTimeOffset comparisonUtc = timeProvider.GetUtcNow();
        StorefrontOrderHistorySnapshot[] primarySnapshots = await LoadPrimaryProjectionSnapshotsAsync(
            normalizedUserId,
            comparisonUtc,
            cancellationToken);

        return new StorefrontOrderHistoryReadResult(
            Orders: primarySnapshots,
            Freshness: CreateFreshnessInfo(
                OrderHistoryReadSource.PrimaryProjection.ToText(),
                primarySnapshots,
                primarySnapshots,
                comparisonUtc),
            ReadPreference: readPreference);
    }

    private async Task<StorefrontOrderHistorySnapshot[]> LoadPrimaryProjectionSnapshotsAsync(
        string normalizedUserId,
        DateTimeOffset projectedUtc,
        CancellationToken cancellationToken)
    {
        await primaryDatabaseInitializer.InitializeAsync(layout.PrimaryDatabasePath, cancellationToken);

        await using PrimaryDbContext dbContext = primaryDbContextFactory.CreateDbContext(layout.PrimaryDatabasePath);
        Order[] orders = await dbContext.Orders
            .AsNoTracking()
            .Include(order => order.Items)
            .ThenInclude(item => item.Product)
            .Include(order => order.Payments)
            .Where(order => order.UserId == normalizedUserId)
            .ToArrayAsync(cancellationToken);

        return orders
            .OrderByDescending(order => order.CreatedUtc)
            .ThenByDescending(order => order.OrderId, StringComparer.Ordinal)
            .Select(order => MapSummary(OrderHistoryProjectionMapper.CreateSummary(order), projectedUtc))
            .ToArray();
    }

    private static StorefrontOrderHistorySnapshot MapRow(ReadModelOrderHistory row)
    {
        OrderHistoryProjectionSummary summary;

        try
        {
            summary = JsonSerializer.Deserialize<OrderHistoryProjectionSummary>(row.SummaryJson, JsonOptions)
                ?? throw new InvalidDataException($"Order-history projection row '{row.OrderId}' could not be deserialized.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Order-history projection row '{row.OrderId}' contains invalid JSON.",
                exception);
        }

        return MapSummary(summary, row.ProjectedUtc);
    }

    private static StorefrontOrderHistorySnapshot MapSummary(
        OrderHistoryProjectionSummary summary,
        DateTimeOffset projectedUtc)
    {
        return new StorefrontOrderHistorySnapshot(
            OrderId: summary.OrderId,
            UserId: summary.UserId,
            Region: summary.Region,
            Status: summary.Status,
            TotalAmountCents: summary.TotalAmountCents,
            ItemCount: summary.ItemCount,
            CreatedUtc: summary.CreatedUtc,
            SubmittedUtc: summary.SubmittedUtc,
            ProjectionVersion: summary.Versions.ProjectionVersion,
            ProjectedUtc: projectedUtc,
            Items: summary.Items
                .Select(item => new StorefrontOrderHistoryItemInfo(
                    ProductId: item.ProductId,
                    ProductName: item.ProductName,
                    Quantity: item.Quantity,
                    UnitPriceCents: item.UnitPriceCents,
                    LineSubtotalCents: item.LineSubtotalCents))
                .ToArray())
        {
            Payment = summary.Payment is null
                ? null
                : new StorefrontOrderHistoryPaymentInfo(
                    PaymentId: summary.Payment.PaymentId,
                    Provider: summary.Payment.Provider,
                    Status: summary.Payment.Status,
                    Mode: summary.Payment.Mode,
                    AmountCents: summary.Payment.AmountCents,
                    ProviderReference: summary.Payment.ProviderReference,
                    ErrorCode: summary.Payment.ErrorCode,
                    AttemptedUtc: summary.Payment.AttemptedUtc,
                    ConfirmedUtc: summary.Payment.ConfirmedUtc)
        };
    }

    private static StorefrontReadFreshnessInfo CreateFreshnessInfo(
        string readSource,
        IReadOnlyList<StorefrontOrderHistorySnapshot> observedSnapshots,
        IReadOnlyList<StorefrontOrderHistorySnapshot> primarySnapshots,
        DateTimeOffset comparisonUtc)
    {
        Dictionary<string, StorefrontOrderHistorySnapshot> observedById = observedSnapshots.ToDictionary(
            snapshot => snapshot.OrderId,
            StringComparer.Ordinal);
        Dictionary<string, StorefrontOrderHistorySnapshot> primaryById = primarySnapshots.ToDictionary(
            snapshot => snapshot.OrderId,
            StringComparer.Ordinal);

        int comparedCount = Math.Max(observedById.Count, primaryById.Count);
        int staleCount = 0;
        double? maxStalenessAgeMs = null;

        foreach ((string orderId, StorefrontOrderHistorySnapshot primarySnapshot) in primaryById)
        {
            if (!observedById.TryGetValue(orderId, out StorefrontOrderHistorySnapshot? observedSnapshot))
            {
                staleCount++;
                maxStalenessAgeMs = Max(
                    maxStalenessAgeMs,
                    Math.Max(0d, (comparisonUtc - new DateTimeOffset(primarySnapshot.ProjectionVersion, TimeSpan.Zero)).TotalMilliseconds));
                continue;
            }

            if (observedSnapshot.ProjectionVersion < primarySnapshot.ProjectionVersion)
            {
                staleCount++;
                maxStalenessAgeMs = Max(
                    maxStalenessAgeMs,
                    Math.Max(
                        0d,
                        new TimeSpan(primarySnapshot.ProjectionVersion - observedSnapshot.ProjectionVersion).TotalMilliseconds));
            }
        }

        foreach (string orderId in observedById.Keys)
        {
            if (!primaryById.ContainsKey(orderId))
            {
                staleCount++;
            }
        }

        return new StorefrontReadFreshnessInfo(
            ReadSource: readSource,
            ComparedCount: comparedCount,
            StaleCount: staleCount,
            StaleFraction: comparedCount > 0 ? staleCount / (double)comparedCount : null,
            MaxStalenessAgeMs: maxStalenessAgeMs,
            ObservedVersion: observedSnapshots.Count > 0 ? observedSnapshots.Max(snapshot => snapshot.ProjectionVersion) : null,
            PrimaryVersion: primarySnapshots.Count > 0 ? primarySnapshots.Max(snapshot => snapshot.ProjectionVersion) : null,
            ObservedUpdatedUtc: observedSnapshots.Count > 0 ? observedSnapshots.Max(snapshot => snapshot.ProjectedUtc) : null,
            PrimaryUpdatedUtc: primarySnapshots.Count > 0 ? primarySnapshots.Max(snapshot => snapshot.ProjectedUtc) : null);
    }

    private static double? Max(double? current, double candidate) =>
        current.HasValue ? Math.Max(current.Value, candidate) : candidate;
}
