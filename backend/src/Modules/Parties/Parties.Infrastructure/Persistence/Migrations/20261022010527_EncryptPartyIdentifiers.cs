using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parties.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EncryptPartyIdentifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_parties_Nni",
                schema: "parties",
                table: "parties");

            migrationBuilder.DropIndex(
                name: "IX_parties_Phone",
                schema: "parties",
                table: "parties");

            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                schema: "parties",
                table: "parties",
                type: "character varying(107)",
                maxLength: 107,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Nni",
                schema: "parties",
                table: "parties",
                type: "character varying(123)",
                maxLength: 123,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NniIndex",
                schema: "parties",
                table: "parties",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneIndex",
                schema: "parties",
                table: "parties",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_parties_NniIndex",
                schema: "parties",
                table: "parties",
                column: "NniIndex",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_parties_PhoneIndex",
                schema: "parties",
                table: "parties",
                column: "PhoneIndex",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_parties_NniIndex",
                schema: "parties",
                table: "parties");

            migrationBuilder.DropIndex(
                name: "IX_parties_PhoneIndex",
                schema: "parties",
                table: "parties");

            migrationBuilder.DropColumn(
                name: "NniIndex",
                schema: "parties",
                table: "parties");

            migrationBuilder.DropColumn(
                name: "PhoneIndex",
                schema: "parties",
                table: "parties");

            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                schema: "parties",
                table: "parties",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(107)",
                oldMaxLength: 107,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Nni",
                schema: "parties",
                table: "parties",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(123)",
                oldMaxLength: 123,
                oldNullable: true);

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
        }
    }
}
