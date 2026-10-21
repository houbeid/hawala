using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cash.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCashBudgetAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Alerts",
                schema: "cash",
                table: "budget_snapshots",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CheckedSince",
                schema: "cash",
                table: "budget_snapshots",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Alerts",
                schema: "cash",
                table: "budget_snapshots");

            migrationBuilder.DropColumn(
                name: "CheckedSince",
                schema: "cash",
                table: "budget_snapshots");
        }
    }
}
