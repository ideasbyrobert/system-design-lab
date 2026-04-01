namespace Lab.Persistence.Projections;

public sealed record ProductPageProjectionRebuildResult(
    string ReadModelDatabasePath,
    string Region,
    int RowsWritten,
    DateTimeOffset ProjectedUtc);
