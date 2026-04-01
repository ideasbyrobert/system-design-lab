using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Lab.Persistence;

public sealed class ReadModelDatabaseInitializer(ReadModelDbContextFactory dbContextFactory)
{
    public async Task InitializeAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        await using ReadModelDbContext dbContext = dbContextFactory.CreateDbContext(databasePath);
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        await EnsureOrderHistoryProjectionSchemaAsync(dbContext, cancellationToken);
    }

    private static async Task EnsureOrderHistoryProjectionSchemaAsync(
        ReadModelDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "ReadModel_OrderHistory" (
                "order_id" TEXT NOT NULL CONSTRAINT "PK_ReadModel_OrderHistory" PRIMARY KEY,
                "user_id" TEXT NOT NULL,
                "region" TEXT NOT NULL,
                "status" TEXT NOT NULL DEFAULT 'Unknown',
                "order_created_utc" TEXT NOT NULL,
                "projection_version" INTEGER NOT NULL,
                "summary_json" TEXT NOT NULL,
                "projected_utc" TEXT NOT NULL
            );
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "ix_readmodel_orderhistory_user_created"
            ON "ReadModel_OrderHistory" ("user_id", "order_created_utc");
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "ix_readmodel_orderhistory_projected_utc"
            ON "ReadModel_OrderHistory" ("projected_utc");
            """,
            cancellationToken);

        await dbContext.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            DbConnection connection = dbContext.Database.GetDbConnection();

            if (!await ColumnExistsAsync(connection, "status", cancellationToken))
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    """
                    ALTER TABLE "ReadModel_OrderHistory"
                    ADD COLUMN "status" TEXT NOT NULL DEFAULT 'Unknown';
                    """,
                    cancellationToken);
            }
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static async Task<bool> ColumnExistsAsync(
        DbConnection connection,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT 1
            FROM pragma_table_info('ReadModel_OrderHistory')
            WHERE name = $columnName
            LIMIT 1;
            """;

        DbParameter columnParameter = command.CreateParameter();
        columnParameter.ParameterName = "$columnName";
        columnParameter.Value = columnName;
        command.Parameters.Add(columnParameter);

        object? scalar = await command.ExecuteScalarAsync(cancellationToken);
        return scalar is not null;
    }
}
