using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Balance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "balance");

            migrationBuilder.CreateTable(
                name: "balance_snapshots",
                schema: "balance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MroBudget = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    XofBudget = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GeneralBalance = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    InitialCapital = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    OwnerContributionsMro = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    CapitalUsed = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    NetResult = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    NetResultPercent = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    SavedProfits = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    SavedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_balance_snapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "capital_changes",
                schema: "balance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SetBy = table.Column<Guid>(type: "uuid", nullable: false),
                    SetAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capital_changes", x => x.Id);
                    table.CheckConstraint("ck_capital_changes_amount_positive", "\"Amount\" > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_balance_snapshots_Date",
                schema: "balance",
                table: "balance_snapshots",
                column: "Date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_capital_changes_SetAt",
                schema: "balance",
                table: "capital_changes",
                column: "SetAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "balance_snapshots",
                schema: "balance");

            migrationBuilder.DropTable(
                name: "capital_changes",
                schema: "balance");
        }
    }
}
