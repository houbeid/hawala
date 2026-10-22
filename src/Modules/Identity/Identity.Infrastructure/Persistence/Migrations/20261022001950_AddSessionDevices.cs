using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                schema: "identity",
                table: "refresh_tokens",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserAgent",
                schema: "identity",
                table: "refresh_tokens",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastLoginAt",
                schema: "identity",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IpAddress",
                schema: "identity",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "UserAgent",
                schema: "identity",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "LastLoginAt",
                schema: "identity",
                table: "accounts");
        }
    }
}
