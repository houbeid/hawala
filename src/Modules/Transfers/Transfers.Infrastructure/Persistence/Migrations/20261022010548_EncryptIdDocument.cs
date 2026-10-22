using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transfers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EncryptIdDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "BeneficiaryIdDocument",
                schema: "transfers",
                table: "transfers",
                type: "character varying(843)",
                maxLength: 843,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "BeneficiaryIdDocument",
                schema: "transfers",
                table: "transfers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(843)",
                oldMaxLength: 843,
                oldNullable: true);
        }
    }
}
