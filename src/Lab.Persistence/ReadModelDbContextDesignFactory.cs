using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lab.Persistence;

public sealed class ReadModelDbContextDesignFactory : IDesignTimeDbContextFactory<ReadModelDbContext>
{
    public ReadModelDbContext CreateDbContext(string[] args)
    {
        string databasePath = args.FirstOrDefault(arg => !string.IsNullOrWhiteSpace(arg))
            ?? Path.Combine(AppContext.BaseDirectory, "design-readmodels.db");

        DbContextOptionsBuilder<ReadModelDbContext> builder = new();
        ReadModelDbContextFactory.Configure(builder, databasePath);
        return new ReadModelDbContext(builder.Options);
    }
}
