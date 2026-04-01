using Lab.Persistence.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lab.Persistence;

public sealed class PrimaryDbContextFactory
{
    public PrimaryDbContext CreateDbContext(string databasePath)
    {
        DbContextOptionsBuilder<PrimaryDbContext> builder = new();
        Configure(builder, databasePath);
        return new PrimaryDbContext(builder.Options);
    }

    internal static void Configure(
        DbContextOptionsBuilder builder,
        string databasePath,
        params IInterceptor[] interceptors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        string fullPath = Path.GetFullPath(databasePath);
        string? directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        SqliteConnectionStringBuilder connectionStringBuilder = new()
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = SqlitePragmaConnectionInterceptor.BusyTimeoutMilliseconds / 1000
        };

        builder.UseSqlite(
            connectionStringBuilder.ToString(),
            sqlite => sqlite.CommandTimeout(SqlitePragmaConnectionInterceptor.BusyTimeoutMilliseconds / 1000));

        builder.AddInterceptors(interceptors.Length > 0 ? interceptors : [new SqlitePragmaConnectionInterceptor()]);
    }
}
