using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lab.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPrimarySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    product_id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    price_cents = table.Column<int>(type: "INTEGER", nullable: false),
                    category = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    version = table.Column<long>(type: "INTEGER", nullable: false),
                    created_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.product_id);
                });

            migrationBuilder.CreateTable(
                name: "queue_jobs",
                columns: table => new
                {
                    queue_job_id = table.Column<string>(type: "TEXT", nullable: false),
                    job_type = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    payload_json = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    available_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    enqueued_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    lease_owner = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    lease_expires_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    started_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    completed_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    retry_count = table.Column<int>(type: "INTEGER", nullable: false),
                    last_error = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_queue_jobs", x => x.queue_job_id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    region = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    created_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "inventory",
                columns: table => new
                {
                    product_id = table.Column<string>(type: "TEXT", nullable: false),
                    available_quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    reserved_quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    version = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory", x => x.product_id);
                    table.ForeignKey(
                        name: "FK_inventory_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "product_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "carts",
                columns: table => new
                {
                    cart_id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    region = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    created_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    updated_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carts", x => x.cart_id);
                    table.ForeignKey(
                        name: "FK_carts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    order_id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    cart_id = table.Column<string>(type: "TEXT", nullable: true),
                    region = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    total_price_cents = table.Column<int>(type: "INTEGER", nullable: false),
                    created_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    submitted_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.order_id);
                    table.ForeignKey(
                        name: "FK_orders_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cart_items",
                columns: table => new
                {
                    cart_item_id = table.Column<string>(type: "TEXT", nullable: false),
                    cart_id = table.Column<string>(type: "TEXT", nullable: false),
                    product_id = table.Column<string>(type: "TEXT", nullable: false),
                    quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    unit_price_cents = table.Column<int>(type: "INTEGER", nullable: false),
                    added_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cart_items", x => x.cart_item_id);
                    table.ForeignKey(
                        name: "FK_cart_items_carts_cart_id",
                        column: x => x.cart_id,
                        principalTable: "carts",
                        principalColumn: "cart_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_cart_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "product_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_items",
                columns: table => new
                {
                    order_item_id = table.Column<string>(type: "TEXT", nullable: false),
                    order_id = table.Column<string>(type: "TEXT", nullable: false),
                    product_id = table.Column<string>(type: "TEXT", nullable: false),
                    quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    unit_price_cents = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_items", x => x.order_item_id);
                    table.ForeignKey(
                        name: "FK_order_items_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_order_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "product_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    payment_id = table.Column<string>(type: "TEXT", nullable: false),
                    order_id = table.Column<string>(type: "TEXT", nullable: false),
                    provider = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    amount_cents = table.Column<int>(type: "INTEGER", nullable: false),
                    external_reference = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    attempted_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    confirmed_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.payment_id);
                    table.ForeignKey(
                        name: "FK_payments_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cart_items_cart_product",
                table: "cart_items",
                columns: new[] { "cart_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cart_items_product_id",
                table: "cart_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_carts_user_status",
                table: "carts",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_order_items_order_product",
                table: "order_items",
                columns: new[] { "order_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "IX_order_items_product_id",
                table: "order_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_user_created",
                table: "orders",
                columns: new[] { "user_id", "created_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_order",
                table: "payments",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_category",
                table: "products",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_queue_jobs_status_available",
                table: "queue_jobs",
                columns: new[] { "status", "available_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cart_items");

            migrationBuilder.DropTable(
                name: "inventory");

            migrationBuilder.DropTable(
                name: "order_items");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "queue_jobs");

            migrationBuilder.DropTable(
                name: "carts");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
