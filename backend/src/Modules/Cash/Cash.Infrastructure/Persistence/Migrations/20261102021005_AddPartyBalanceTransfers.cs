using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cash.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPartyBalanceTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "party_balance_transfers",
                schema: "cash",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromPartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToPartyId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_party_balance_transfers", x => x.Id);
                    table.CheckConstraint("ck_party_balance_transfers_amount_positive", "\"Amount\" > 0");
                    table.CheckConstraint("ck_party_balance_transfers_two_parties", "\"FromPartyId\" <> \"ToPartyId\"");
                });

            migrationBuilder.CreateIndex(
                name: "IX_party_balance_transfers_AgencyId_OperationAt",
                schema: "cash",
                table: "party_balance_transfers",
                columns: new[] { "AgencyId", "OperationAt" });

            migrationBuilder.CreateIndex(
                name: "IX_party_balance_transfers_CreatedBy_IdempotencyKey",
                schema: "cash",
                table: "party_balance_transfers",
                columns: new[] { "CreatedBy", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_party_balance_transfers_FromPartyId",
                schema: "cash",
                table: "party_balance_transfers",
                column: "FromPartyId");

            migrationBuilder.CreateIndex(
                name: "IX_party_balance_transfers_ToPartyId",
                schema: "cash",
                table: "party_balance_transfers",
                column: "ToPartyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "party_balance_transfers",
                schema: "cash");
        }
    }
}
