using Lab.Persistence.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lab.Persistence;

public sealed class ReadModelDbContextFactory
{
    public ReadModelDbContext CreateDbContext(string databasePath)
    {
        DbContextOptionsBuilder<ReadModelDbContext> builder = new();
        Configure(builder, databasePath);
        return new ReadModelDbContext(builder.Options);
    }

    internal static void Configure(
        DbContextOptionsBuilder builder,
        string databasePath,
        params IInterceptor[] interceptors)
    {
        PrimaryDbContextFactory.Configure(builder, databasePath, interceptors);
    }
}
