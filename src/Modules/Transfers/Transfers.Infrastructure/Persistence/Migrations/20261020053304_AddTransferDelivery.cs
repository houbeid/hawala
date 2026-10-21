using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transfers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeliveredAt",
                schema: "transfers",
                table: "transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeliveredBy",
                schema: "transfers",
                table: "transfers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryEntryId",
                schema: "transfers",
                table: "transfers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryNote",
                schema: "transfers",
                table: "transfers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeliveryOperationAt",
                schema: "transfers",
                table: "transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_transfers_code_attempts",
                schema: "transfers",
                table: "transfers",
                sql: "\"FailedCodeAttempts\" BETWEEN 0 AND 5");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transfers_delivery",
                schema: "transfers",
                table: "transfers",
                sql: "(\"Status\" = 'Delivered') = (\"DeliveredAt\" IS NOT NULL AND \"DeliveryEntryId\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_transfers_code_attempts",
                schema: "transfers",
                table: "transfers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transfers_delivery",
                schema: "transfers",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                schema: "transfers",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "DeliveredBy",
                schema: "transfers",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "DeliveryEntryId",
                schema: "transfers",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "DeliveryNote",
                schema: "transfers",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "DeliveryOperationAt",
                schema: "transfers",
                table: "transfers");
        }
    }
}
