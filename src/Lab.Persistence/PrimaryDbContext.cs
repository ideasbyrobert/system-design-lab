using Lab.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lab.Persistence;

public sealed class PrimaryDbContext(DbContextOptions<PrimaryDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<InventoryRecord> Inventory => Set<InventoryRecord>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Cart> Carts => Set<Cart>();

    public DbSet<CartItem> CartItems => Set<CartItem>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<QueueJob> QueueJobs => Set<QueueJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products");
            entity.HasKey(item => item.ProductId);
            entity.Property(item => item.ProductId).HasColumnName("product_id").ValueGeneratedNever();
            entity.Property(item => item.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
            entity.Property(item => item.Description).HasColumnName("description").HasMaxLength(2048).IsRequired();
            entity.Property(item => item.PriceCents).HasColumnName("price_cents").IsRequired();
            entity.Property(item => item.Category).HasColumnName("category").HasMaxLength(128).IsRequired();
            entity.Property(item => item.Version).HasColumnName("version").IsRequired();
            entity.Property(item => item.CreatedUtc).HasColumnName("created_utc").IsRequired();
            entity.Property(item => item.UpdatedUtc).HasColumnName("updated_utc").IsRequired();
            entity.HasIndex(item => item.Category).HasDatabaseName("ix_products_category");
        });

        modelBuilder.Entity<InventoryRecord>(entity =>
        {
            entity.ToTable("inventory", table =>
            {
                table.HasCheckConstraint("ck_inventory_available_quantity_nonnegative", "available_quantity >= 0");
                table.HasCheckConstraint("ck_inventory_reserved_quantity_nonnegative", "reserved_quantity >= 0");
            });
            entity.HasKey(item => item.ProductId);
            entity.Property(item => item.ProductId).HasColumnName("product_id").ValueGeneratedNever();
            entity.Property(item => item.AvailableQuantity).HasColumnName("available_quantity").IsRequired();
            entity.Property(item => item.ReservedQuantity).HasColumnName("reserved_quantity").IsRequired();
            entity.Property(item => item.Version).HasColumnName("version").IsRequired();
            entity.Property(item => item.UpdatedUtc).HasColumnName("updated_utc").IsRequired();
            entity.HasOne(item => item.Product)
                .WithOne(product => product.Inventory)
                .HasForeignKey<InventoryRecord>(item => item.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(item => item.UserId);
            entity.Property(item => item.UserId).HasColumnName("user_id").ValueGeneratedNever();
            entity.Property(item => item.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
            entity.Property(item => item.DisplayName).HasColumnName("display_name").HasMaxLength(256).IsRequired();
            entity.Property(item => item.Region).HasColumnName("region").HasMaxLength(64).IsRequired();
            entity.Property(item => item.CreatedUtc).HasColumnName("created_utc").IsRequired();
            entity.HasIndex(item => item.Email).IsUnique().HasDatabaseName("ix_users_email");
        });

        modelBuilder.Entity<Cart>(entity =>
        {
            entity.ToTable("carts");
            entity.HasKey(item => item.CartId);
            entity.Property(item => item.CartId).HasColumnName("cart_id").ValueGeneratedNever();
            entity.Property(item => item.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(item => item.Region).HasColumnName("region").HasMaxLength(64).IsRequired();
            entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(64).IsRequired();
            entity.Property(item => item.CreatedUtc).HasColumnName("created_utc").IsRequired();
            entity.Property(item => item.UpdatedUtc).HasColumnName("updated_utc").IsRequired();
            entity.HasOne(item => item.User)
                .WithMany(user => user.Carts)
                .HasForeignKey(item => item.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.UserId, item.Status }).HasDatabaseName("ix_carts_user_status");
        });

        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.ToTable("cart_items");
            entity.HasKey(item => item.CartItemId);
            entity.Property(item => item.CartItemId).HasColumnName("cart_item_id").ValueGeneratedNever();
            entity.Property(item => item.CartId).HasColumnName("cart_id").IsRequired();
            entity.Property(item => item.ProductId).HasColumnName("product_id").IsRequired();
            entity.Property(item => item.Quantity).HasColumnName("quantity").IsRequired();
            entity.Property(item => item.UnitPriceCents).HasColumnName("unit_price_cents").IsRequired();
            entity.Property(item => item.AddedUtc).HasColumnName("added_utc").IsRequired();
            entity.HasOne(item => item.Cart)
                .WithMany(cart => cart.Items)
                .HasForeignKey(item => item.CartId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Product)
                .WithMany(product => product.CartItems)
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.CartId, item.ProductId }).IsUnique().HasDatabaseName("ix_cart_items_cart_product");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(item => item.OrderId);
            entity.Property(item => item.OrderId).HasColumnName("order_id").ValueGeneratedNever();
            entity.Property(item => item.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(item => item.CartId).HasColumnName("cart_id");
            entity.Property(item => item.Region).HasColumnName("region").HasMaxLength(64).IsRequired();
            entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(64).IsRequired();
            entity.Property(item => item.TotalPriceCents).HasColumnName("total_price_cents").IsRequired();
            entity.Property(item => item.CreatedUtc).HasColumnName("created_utc").IsRequired();
            entity.Property(item => item.SubmittedUtc).HasColumnName("submitted_utc");
            entity.HasOne(item => item.User)
                .WithMany(user => user.Orders)
                .HasForeignKey(item => item.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.UserId, item.CreatedUtc }).HasDatabaseName("ix_orders_user_created");
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("order_items");
            entity.HasKey(item => item.OrderItemId);
            entity.Property(item => item.OrderItemId).HasColumnName("order_item_id").ValueGeneratedNever();
            entity.Property(item => item.OrderId).HasColumnName("order_id").IsRequired();
            entity.Property(item => item.ProductId).HasColumnName("product_id").IsRequired();
            entity.Property(item => item.Quantity).HasColumnName("quantity").IsRequired();
            entity.Property(item => item.UnitPriceCents).HasColumnName("unit_price_cents").IsRequired();
            entity.HasOne(item => item.Order)
                .WithMany(order => order.Items)
                .HasForeignKey(item => item.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Product)
                .WithMany(product => product.OrderItems)
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.OrderId, item.ProductId }).HasDatabaseName("ix_order_items_order_product");
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("payments");
            entity.HasKey(item => item.PaymentId);
            entity.Property(item => item.PaymentId).HasColumnName("payment_id").ValueGeneratedNever();
            entity.Property(item => item.OrderId).HasColumnName("order_id").IsRequired();
            entity.Property(item => item.Provider).HasColumnName("provider").HasMaxLength(128).IsRequired();
            entity.Property(item => item.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
            entity.Property(item => item.Mode).HasColumnName("mode").HasMaxLength(64);
            entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(64).IsRequired();
            entity.Property(item => item.AmountCents).HasColumnName("amount_cents").IsRequired();
            entity.Property(item => item.ExternalReference).HasColumnName("external_reference").HasMaxLength(256);
            entity.Property(item => item.ErrorCode).HasColumnName("error_code").HasMaxLength(128);
            entity.Property(item => item.AttemptedUtc).HasColumnName("attempted_utc").IsRequired();
            entity.Property(item => item.ConfirmedUtc).HasColumnName("confirmed_utc");
            entity.HasOne(item => item.Order)
                .WithMany(order => order.Payments)
                .HasForeignKey(item => item.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => item.OrderId).HasDatabaseName("ix_payments_order");
            entity.HasIndex(item => item.IdempotencyKey).IsUnique().HasDatabaseName("ix_payments_idempotency_key");
        });

        modelBuilder.Entity<QueueJob>(entity =>
        {
            entity.ToTable("queue_jobs");
            entity.HasKey(item => item.QueueJobId);
            entity.Property(item => item.QueueJobId).HasColumnName("queue_job_id").ValueGeneratedNever();
            entity.Property(item => item.JobType).HasColumnName("job_type").HasMaxLength(128).IsRequired();
            entity.Property(item => item.PayloadJson).HasColumnName("payload_json").IsRequired();
            entity.Property(item => item.Status).HasColumnName("status").HasMaxLength(64).IsRequired();
            entity.Property(item => item.AvailableUtc).HasColumnName("available_utc").IsRequired();
            entity.Property(item => item.EnqueuedUtc).HasColumnName("enqueued_utc").IsRequired();
            entity.Property(item => item.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(128);
            entity.Property(item => item.LeaseExpiresUtc).HasColumnName("lease_expires_utc");
            entity.Property(item => item.StartedUtc).HasColumnName("started_utc");
            entity.Property(item => item.CompletedUtc).HasColumnName("completed_utc");
            entity.Property(item => item.RetryCount).HasColumnName("retry_count").IsRequired();
            entity.Property(item => item.LastError).HasColumnName("last_error").HasMaxLength(2048);
            entity.HasIndex(item => new { item.Status, item.AvailableUtc }).HasDatabaseName("ix_queue_jobs_status_available");
        });
    }
}
