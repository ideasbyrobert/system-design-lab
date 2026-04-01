using Microsoft.EntityFrameworkCore;

namespace Lab.Persistence;

public sealed class PrimaryDatabaseInitializer(PrimaryDbContextFactory dbContextFactory)
{
    public async Task InitializeAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        await using PrimaryDbContext dbContext = dbContextFactory.CreateDbContext(databasePath);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
