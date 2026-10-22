using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transfers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferCodeActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BeneficiaryIdDocument",
                schema: "transfers",
                table: "transfers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CodeReplacedAt",
                schema: "transfers",
                table: "transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CodeReplacedBy",
                schema: "transfers",
                table: "transfers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodeUnlockReason",
                schema: "transfers",
                table: "transfers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CodeUnlockedAt",
                schema: "transfers",
                table: "transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CodeUnlockedBy",
                schema: "transfers",
                table: "transfers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DeliveredWithoutCode",
                schema: "transfers",
                table: "transfers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryWithoutCodeReason",
                schema: "transfers",
                table: "transfers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_transfers_without_code",
                schema: "transfers",
                table: "transfers",
                sql: "NOT \"DeliveredWithoutCode\" OR (\"Status\" = 'Delivered' AND \"BeneficiaryIdDocument\" IS NOT NULL AND \"DeliveryWithoutCodeReason\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_transfers_without_code",
                schema: "transfers",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "BeneficiaryIdDocument",
                schema: "transfers",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "CodeReplacedAt",
                schema: "transfers",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "CodeReplacedBy",
                schema: "transfers",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "CodeUnlockReason",
                schema: "transfers",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "CodeUnlockedAt",
                schema: "transfers",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "CodeUnlockedBy",
                schema: "transfers",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "DeliveredWithoutCode",
                schema: "transfers",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "DeliveryWithoutCodeReason",
                schema: "transfers",
                table: "transfers");
        }
    }
}
