namespace Lab.Persistence.Projections;

public sealed record OrderHistoryProjectionRebuildResult(
    string ReadModelDatabasePath,
    string? UserId,
    string? OrderId,
    int RowsWritten,
    DateTimeOffset ProjectedUtc,
    bool ProjectionRowWritten);
