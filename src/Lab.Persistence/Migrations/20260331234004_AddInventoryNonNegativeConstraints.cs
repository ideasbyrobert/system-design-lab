using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lab.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryNonNegativeConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_available_quantity_nonnegative",
                table: "inventory",
                sql: "available_quantity >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_reserved_quantity_nonnegative",
                table: "inventory",
                sql: "reserved_quantity >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_available_quantity_nonnegative",
                table: "inventory");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_reserved_quantity_nonnegative",
                table: "inventory");
        }
    }
}
