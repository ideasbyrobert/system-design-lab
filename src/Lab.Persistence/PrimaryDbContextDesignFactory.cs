using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lab.Persistence;

public sealed class PrimaryDbContextDesignFactory : IDesignTimeDbContextFactory<PrimaryDbContext>
{
    public PrimaryDbContext CreateDbContext(string[] args)
    {
        string databasePath = args.FirstOrDefault(arg => !string.IsNullOrWhiteSpace(arg))
            ?? Path.Combine(AppContext.BaseDirectory, "design-primary.db");

        DbContextOptionsBuilder<PrimaryDbContext> builder = new();
        PrimaryDbContextFactory.Configure(builder, databasePath);
        return new PrimaryDbContext(builder.Options);
    }
}
