using Lab.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lab.Persistence;

public sealed class ReadModelDbContext(DbContextOptions<ReadModelDbContext> options) : DbContext(options)
{
    public DbSet<ReadModelProductPage> ProductPages => Set<ReadModelProductPage>();

    public DbSet<ReadModelOrderHistory> OrderHistories => Set<ReadModelOrderHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReadModelProductPage>(entity =>
        {
            entity.ToTable("ReadModel_ProductPage");
            entity.HasKey(item => new { item.ProductId, item.Region });
            entity.Property(item => item.ProductId).HasColumnName("product_id").HasMaxLength(128).IsRequired();
            entity.Property(item => item.Region).HasColumnName("region").HasMaxLength(64).IsRequired();
            entity.Property(item => item.ProjectionVersion).HasColumnName("projection_version").IsRequired();
            entity.Property(item => item.SummaryJson).HasColumnName("summary_json").IsRequired();
            entity.Property(item => item.ProjectedUtc).HasColumnName("projected_utc").IsRequired();
            entity.HasIndex(item => item.Region).HasDatabaseName("ix_readmodel_productpage_region");
            entity.HasIndex(item => item.ProjectedUtc).HasDatabaseName("ix_readmodel_productpage_projected_utc");
        });

        modelBuilder.Entity<ReadModelOrderHistory>(entity =>
        {
            entity.ToTable("ReadModel_OrderHistory");
            entity.HasKey(item => item.OrderId);
            entity.Property(item => item.OrderId).HasColumnName("order_id").HasMaxLength(128).IsRequired();
            entity.Property(item => item.UserId).HasColumnName("user_id").HasMaxLength(128).IsRequired();
            entity.Property(item => item.Region).HasColumnName("region").HasMaxLength(64).IsRequired();
            entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(64).IsRequired();
            entity.Property(item => item.OrderCreatedUtc).HasColumnName("order_created_utc").IsRequired();
            entity.Property(item => item.ProjectionVersion).HasColumnName("projection_version").IsRequired();
            entity.Property(item => item.SummaryJson).HasColumnName("summary_json").IsRequired();
            entity.Property(item => item.ProjectedUtc).HasColumnName("projected_utc").IsRequired();
            entity.HasIndex(item => new { item.UserId, item.OrderCreatedUtc }).HasDatabaseName("ix_readmodel_orderhistory_user_created");
            entity.HasIndex(item => item.ProjectedUtc).HasDatabaseName("ix_readmodel_orderhistory_projected_utc");
        });
    }
}
