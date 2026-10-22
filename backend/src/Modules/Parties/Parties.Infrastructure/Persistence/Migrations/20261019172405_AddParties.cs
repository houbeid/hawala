using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parties.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "parties");

            migrationBuilder.CreateTable(
                name: "parties",
                schema: "parties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Nni = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    InBalanceSheet = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parties", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "party_agency_accounts",
                schema: "parties",
                columns: table => new
                {
                    AgencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    LedgerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditLimit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_party_agency_accounts", x => new { x.PartyId, x.AgencyId });
                    table.ForeignKey(
                        name: "FK_party_agency_accounts_parties_PartyId",
                        column: x => x.PartyId,
                        principalSchema: "parties",
                        principalTable: "parties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_parties_Name",
                schema: "parties",
                table: "parties",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_parties_Nni",
                schema: "parties",
                table: "parties",
                column: "Nni",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_parties_Phone",
                schema: "parties",
                table: "parties",
                column: "Phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_party_agency_accounts_LedgerAccountId",
                schema: "parties",
                table: "party_agency_accounts",
                column: "LedgerAccountId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "party_agency_accounts",
                schema: "parties");

            migrationBuilder.DropTable(
                name: "parties",
                schema: "parties");
        }
    }
}
