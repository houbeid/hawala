using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Profit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "profit");

            migrationBuilder.CreateTable(
                name: "profit_snapshots",
                schema: "profit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    TransferCount = table.Column<int>(type: "integer", nullable: false),
                    AmountXof = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PurchaseRate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ProfitMro = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: false),
                    SavedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profit_snapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_profit_snapshots_AgencyId_Date",
                schema: "profit",
                table: "profit_snapshots",
                columns: new[] { "AgencyId", "Date" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "profit_snapshots",
                schema: "profit");
        }
    }
}
