using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Balance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGeneralBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "general_balance_snapshots",
                schema: "balance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NouakchottCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NouakchottBanks = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NouakchottDebtors = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NouakchottCreditors = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NouakchottUndelivered = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NouakchottOwnerContributions = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DakarCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DakarBanks = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DakarDebtors = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DakarCreditors = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DakarUndelivered = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DakarOwnerContributions = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GeneralBalance = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    Capital = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CapitalUsed = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    NetResult = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    NetResultPercent = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    SavedProfitsMro = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    SavedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_general_balance_snapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "general_capital_changes",
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
                    table.PrimaryKey("PK_general_capital_changes", x => x.Id);
                    table.CheckConstraint("ck_general_capital_changes_amount_positive", "\"Amount\" > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_general_balance_snapshots_Date",
                schema: "balance",
                table: "general_balance_snapshots",
                column: "Date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_general_capital_changes_SetAt",
                schema: "balance",
                table: "general_capital_changes",
                column: "SetAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "general_balance_snapshots",
                schema: "balance");

            migrationBuilder.DropTable(
                name: "general_capital_changes",
                schema: "balance");
        }
    }
}
