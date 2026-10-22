using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parties.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPartyScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_party_scopes",
                schema: "parties",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsLimited = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_party_scopes", x => x.AccountId);
                });

            migrationBuilder.CreateTable(
                name: "account_allowed_parties",
                schema: "parties",
                columns: table => new
                {
                    PartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_allowed_parties", x => new { x.AccountId, x.PartyId });
                    table.ForeignKey(
                        name: "FK_account_allowed_parties_account_party_scopes_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "parties",
                        principalTable: "account_party_scopes",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_allowed_parties_PartyId",
                schema: "parties",
                table: "account_allowed_parties",
                column: "PartyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_allowed_parties",
                schema: "parties");

            migrationBuilder.DropTable(
                name: "account_party_scopes",
                schema: "parties");
        }
    }
}
