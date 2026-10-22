using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transfers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CancellationEntryId",
                schema: "transfers",
                table: "transfers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancellationOperationAt",
                schema: "transfers",
                table: "transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                schema: "transfers",
                table: "transfers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                schema: "transfers",
                table: "transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledBy",
                schema: "transfers",
                table: "transfers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_transfers_cancellation",
                schema: "transfers",
                table: "transfers",
                sql: "(\"Status\" = 'Cancelled') = (\"CancelledAt\" IS NOT NULL AND \"CancellationReason\" IS NOT NULL AND \"CancellationEntryId\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_transfers_cancellation",
                schema: "transfers",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "CancellationEntryId",
                schema: "transfers",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "CancellationOperationAt",
                schema: "transfers",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                schema: "transfers",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                schema: "transfers",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                schema: "transfers",
                table: "transfers");
        }
    }
}
