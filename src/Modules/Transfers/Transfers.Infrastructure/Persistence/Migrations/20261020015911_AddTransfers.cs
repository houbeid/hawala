using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transfers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "transfers");

            migrationBuilder.CreateTable(
                name: "reference_counters",
                schema: "transfers",
                columns: table => new
                {
                    AgencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    LastValue = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_counters", x => new { x.AgencyId, x.Day });
                });

            migrationBuilder.CreateTable(
                name: "transfers",
                schema: "transfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SourceAgencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    DestinationAgencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    AmountXof = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AmountMro = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SaleRate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PurchaseRate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SenderInfo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BeneficiaryInfo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OperationAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    WithdrawalCodeHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FailedCodeAttempts = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreationEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfers", x => x.Id);
                    table.CheckConstraint("ck_transfers_amounts_positive", "\"AmountXof\" > 0 AND \"AmountMro\" > 0");
                    table.CheckConstraint("ck_transfers_corridor", "\"SourceAgencyId\" <> \"DestinationAgencyId\" AND \"SourceCurrency\" <> \"DestinationCurrency\"");
                });

            migrationBuilder.CreateTable(
                name: "transfer_payments",
                schema: "transfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TreasuryAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    TransferId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfer_payments", x => x.Id);
                    table.CheckConstraint("ck_transfer_payments_amount_positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_transfer_payments_transfers_TransferId",
                        column: x => x.TransferId,
                        principalSchema: "transfers",
                        principalTable: "transfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transfer_payments_TransferId",
                schema: "transfers",
                table: "transfer_payments",
                column: "TransferId");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_payments_TreasuryAccountId",
                schema: "transfers",
                table: "transfer_payments",
                column: "TreasuryAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_transfers_CreatedBy_IdempotencyKey",
                schema: "transfers",
                table: "transfers",
                columns: new[] { "CreatedBy", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transfers_DestinationAgencyId_OperationAt",
                schema: "transfers",
                table: "transfers",
                columns: new[] { "DestinationAgencyId", "OperationAt" });

            migrationBuilder.CreateIndex(
                name: "IX_transfers_Reference",
                schema: "transfers",
                table: "transfers",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transfers_SourceAgencyId_OperationAt",
                schema: "transfers",
                table: "transfers",
                columns: new[] { "SourceAgencyId", "OperationAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reference_counters",
                schema: "transfers");

            migrationBuilder.DropTable(
                name: "transfer_payments",
                schema: "transfers");

            migrationBuilder.DropTable(
                name: "transfers",
                schema: "transfers");
        }
    }
}
