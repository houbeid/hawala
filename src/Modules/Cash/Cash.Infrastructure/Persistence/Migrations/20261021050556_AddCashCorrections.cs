using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cash.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCashCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CancellationEntryId",
                schema: "cash",
                table: "cash_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                schema: "cash",
                table: "cash_movements",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                schema: "cash",
                table: "cash_movements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledBy",
                schema: "cash",
                table: "cash_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "cash",
                table: "cash_movements",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValueSql: "'Active'");

            migrationBuilder.CreateTable(
                name: "balance_adjustments",
                schema: "cash",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Target = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OperationAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_balance_adjustments", x => x.Id);
                    table.CheckConstraint("ck_balance_adjustments_amount_not_zero", "\"Amount\" <> 0");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_cash_movements_cancellation",
                schema: "cash",
                table: "cash_movements",
                sql: "(\"Status\" = 'Cancelled') = (\"CancelledAt\" IS NOT NULL AND \"CancellationReason\" IS NOT NULL AND \"CancellationEntryId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_balance_adjustments_AgencyId_OperationAt",
                schema: "cash",
                table: "balance_adjustments",
                columns: new[] { "AgencyId", "OperationAt" });

            migrationBuilder.CreateIndex(
                name: "IX_balance_adjustments_TargetId",
                schema: "cash",
                table: "balance_adjustments",
                column: "TargetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "balance_adjustments",
                schema: "cash");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cash_movements_cancellation",
                schema: "cash",
                table: "cash_movements");

            migrationBuilder.DropColumn(
                name: "CancellationEntryId",
                schema: "cash",
                table: "cash_movements");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                schema: "cash",
                table: "cash_movements");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                schema: "cash",
                table: "cash_movements");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                schema: "cash",
                table: "cash_movements");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "cash",
                table: "cash_movements");
        }
    }
}
