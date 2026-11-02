using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parties.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPartyHomeAgency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "HomeAgencyId",
                schema: "parties",
                table: "parties",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_parties_HomeAgencyId",
                schema: "parties",
                table: "parties",
                column: "HomeAgencyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_parties_HomeAgencyId",
                schema: "parties",
                table: "parties");

            migrationBuilder.DropColumn(
                name: "HomeAgencyId",
                schema: "parties",
                table: "parties");
        }
    }
}
