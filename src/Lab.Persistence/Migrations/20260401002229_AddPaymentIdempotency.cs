using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lab.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "error_code",
                table: "payments",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "payments",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mode",
                table: "payments",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_idempotency_key",
                table: "payments",
                column: "idempotency_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payments_idempotency_key",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "error_code",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "mode",
                table: "payments");
        }
    }
}
