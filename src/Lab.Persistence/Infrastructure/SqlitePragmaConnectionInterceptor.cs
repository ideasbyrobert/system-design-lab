using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lab.Persistence.Infrastructure;

internal sealed class SqlitePragmaConnectionInterceptor : DbConnectionInterceptor
{
    public const int BusyTimeoutMilliseconds = 5_000;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        ApplyPragmas(connection);

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default) =>
        await ApplyPragmasAsync(connection, cancellationToken);

    private static void ApplyPragmas(DbConnection connection)
    {
        if (connection is not SqliteConnection sqliteConnection)
        {
            return;
        }

        using SqliteCommand command = sqliteConnection.CreateCommand();
        command.CommandText =
            $"""
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = {BusyTimeoutMilliseconds};
            PRAGMA journal_mode = WAL;
            """;
        command.ExecuteNonQuery();
    }

    private static async Task ApplyPragmasAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection is not SqliteConnection sqliteConnection)
        {
            return;
        }

        await using SqliteCommand command = sqliteConnection.CreateCommand();
        command.CommandText =
            $"""
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = {BusyTimeoutMilliseconds};
            PRAGMA journal_mode = WAL;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
