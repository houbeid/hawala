using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cash.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cash");

            migrationBuilder.CreateTable(
                name: "cash_movements",
                schema: "cash",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TreasuryAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OperationAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_movements", x => x.Id);
                    table.CheckConstraint("ck_cash_movements_amount_positive", "\"Amount\" > 0");
                    table.CheckConstraint("ck_cash_movements_party", "(\"Type\" IN ('Receipt', 'Payment')) = (\"PartyId\" IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_cash_movements_AgencyId_OperationAt",
                schema: "cash",
                table: "cash_movements",
                columns: new[] { "AgencyId", "OperationAt" });

            migrationBuilder.CreateIndex(
                name: "IX_cash_movements_CreatedBy_IdempotencyKey",
                schema: "cash",
                table: "cash_movements",
                columns: new[] { "CreatedBy", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cash_movements_PartyId",
                schema: "cash",
                table: "cash_movements",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_cash_movements_TreasuryAccountId",
                schema: "cash",
                table: "cash_movements",
                column: "TreasuryAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_movements",
                schema: "cash");
        }
    }
}
