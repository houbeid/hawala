using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transfers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferCredit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CreditAmount",
                schema: "transfers",
                table: "transfers",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "CreditPartyId",
                schema: "transfers",
                table: "transfers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_transfers_CreditPartyId",
                schema: "transfers",
                table: "transfers",
                column: "CreditPartyId");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transfers_credit",
                schema: "transfers",
                table: "transfers",
                sql: "(\"CreditPartyId\" IS NULL AND \"CreditAmount\" = 0) OR (\"CreditPartyId\" IS NOT NULL AND \"CreditAmount\" > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_transfers_CreditPartyId",
                schema: "transfers",
                table: "transfers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transfers_credit",
                schema: "transfers",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "CreditAmount",
                schema: "transfers",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "CreditPartyId",
                schema: "transfers",
                table: "transfers");
        }
    }
}
