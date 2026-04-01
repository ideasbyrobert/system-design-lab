using Lab.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lab.Persistence.Projections;

public sealed class OrderHistoryProjectionRebuilder(
    PrimaryDatabaseInitializer primaryDatabaseInitializer,
    PrimaryDbContextFactory primaryDbContextFactory,
    ReadModelDatabaseInitializer readModelDatabaseInitializer,
    ReadModelDbContextFactory readModelDbContextFactory,
    TimeProvider timeProvider)
{
    public async Task<OrderHistoryProjectionRebuildResult> RebuildAsync(
        string primaryDatabasePath,
        string readModelDatabasePath,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryDatabasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(readModelDatabasePath);

        string normalizedReadModelPath = Path.GetFullPath(readModelDatabasePath);
        string? normalizedUserId = NormalizeOptionalText(userId);

        await primaryDatabaseInitializer.InitializeAsync(primaryDatabasePath, cancellationToken);
        await readModelDatabaseInitializer.InitializeAsync(normalizedReadModelPath, cancellationToken);

        await using PrimaryDbContext primaryDbContext = primaryDbContextFactory.CreateDbContext(primaryDatabasePath);

        IQueryable<Order> query = primaryDbContext.Orders
            .AsNoTracking()
            .Include(order => order.Items)
            .ThenInclude(item => item.Product)
            .Include(order => order.Payments);

        if (normalizedUserId is not null)
        {
            query = query.Where(order => order.UserId == normalizedUserId);
        }

        Order[] orders = (await query.ToListAsync(cancellationToken))
            .OrderByDescending(order => order.CreatedUtc)
            .ThenByDescending(order => order.OrderId, StringComparer.Ordinal)
            .ToArray();

        DateTimeOffset projectedUtc = timeProvider.GetUtcNow();
        ReadModelOrderHistory[] projectionRows = orders
            .Select(order => OrderHistoryProjectionMapper.CreateProjectionRow(order, projectedUtc))
            .ToArray();

        await using ReadModelDbContext readModelDbContext = readModelDbContextFactory.CreateDbContext(normalizedReadModelPath);
        await using var transaction = await readModelDbContext.Database.BeginTransactionAsync(cancellationToken);

        if (normalizedUserId is null)
        {
            await readModelDbContext.OrderHistories.ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            await readModelDbContext.OrderHistories
                .Where(row => row.UserId == normalizedUserId)
                .ExecuteDeleteAsync(cancellationToken);
        }

        await readModelDbContext.OrderHistories.AddRangeAsync(projectionRows, cancellationToken);
        await readModelDbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new OrderHistoryProjectionRebuildResult(
            ReadModelDatabasePath: normalizedReadModelPath,
            UserId: normalizedUserId,
            OrderId: null,
            RowsWritten: projectionRows.Length,
            ProjectedUtc: projectedUtc,
            ProjectionRowWritten: projectionRows.Length > 0);
    }

    public async Task<OrderHistoryProjectionRebuildResult> UpdateAsync(
        string primaryDatabasePath,
        string readModelDatabasePath,
        string orderId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryDatabasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(readModelDatabasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        string normalizedReadModelPath = Path.GetFullPath(readModelDatabasePath);
        string normalizedOrderId = orderId.Trim();
        string normalizedUserId = userId.Trim();

        await primaryDatabaseInitializer.InitializeAsync(primaryDatabasePath, cancellationToken);
        await readModelDatabaseInitializer.InitializeAsync(normalizedReadModelPath, cancellationToken);

        await using PrimaryDbContext primaryDbContext = primaryDbContextFactory.CreateDbContext(primaryDatabasePath);
        Order? order = await primaryDbContext.Orders
            .AsNoTracking()
            .Include(item => item.Items)
            .ThenInclude(item => item.Product)
            .Include(item => item.Payments)
            .SingleOrDefaultAsync(
                item => item.OrderId == normalizedOrderId && item.UserId == normalizedUserId,
                cancellationToken);

        DateTimeOffset projectedUtc = timeProvider.GetUtcNow();

        await using ReadModelDbContext readModelDbContext = readModelDbContextFactory.CreateDbContext(normalizedReadModelPath);
        await using var transaction = await readModelDbContext.Database.BeginTransactionAsync(cancellationToken);

        await readModelDbContext.OrderHistories
            .Where(row => row.OrderId == normalizedOrderId)
            .ExecuteDeleteAsync(cancellationToken);

        int rowsWritten = 0;

        if (order is not null)
        {
            await readModelDbContext.OrderHistories.AddAsync(
                OrderHistoryProjectionMapper.CreateProjectionRow(order, projectedUtc),
                cancellationToken);
            rowsWritten = 1;
        }

        await readModelDbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new OrderHistoryProjectionRebuildResult(
            ReadModelDatabasePath: normalizedReadModelPath,
            UserId: normalizedUserId,
            OrderId: normalizedOrderId,
            RowsWritten: rowsWritten,
            ProjectedUtc: projectedUtc,
            ProjectionRowWritten: rowsWritten == 1);
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
