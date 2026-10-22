using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cash.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCashInternalTransfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Type",
                schema: "cash",
                table: "cash_movements",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<Guid>(
                name: "DestinationTreasuryAccountId",
                schema: "cash",
                table: "cash_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_cash_movements_DestinationTreasuryAccountId",
                schema: "cash",
                table: "cash_movements",
                column: "DestinationTreasuryAccountId");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cash_movements_destination",
                schema: "cash",
                table: "cash_movements",
                sql: "(\"Type\" = 'InternalTransfer') = (\"DestinationTreasuryAccountId\" IS NOT NULL) AND (\"DestinationTreasuryAccountId\" IS NULL OR \"DestinationTreasuryAccountId\" <> \"TreasuryAccountId\")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_cash_movements_DestinationTreasuryAccountId",
                schema: "cash",
                table: "cash_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cash_movements_destination",
                schema: "cash",
                table: "cash_movements");

            migrationBuilder.DropColumn(
                name: "DestinationTreasuryAccountId",
                schema: "cash",
                table: "cash_movements");

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                schema: "cash",
                table: "cash_movements",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);
        }
    }
}
