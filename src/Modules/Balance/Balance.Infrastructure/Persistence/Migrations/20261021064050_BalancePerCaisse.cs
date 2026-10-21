using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Balance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BalancePerCaisse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ajouté à la main : les saisies et photos de l'ancien bilan converti n'ont ni caisse ni devise (données de dev seulement).
            migrationBuilder.Sql("DELETE FROM balance.balance_snapshots; DELETE FROM balance.capital_changes;");

            migrationBuilder.DropIndex(
                name: "IX_capital_changes_SetAt",
                schema: "balance",
                table: "capital_changes");

            migrationBuilder.DropIndex(
                name: "IX_balance_snapshots_Date",
                schema: "balance",
                table: "balance_snapshots");

            migrationBuilder.DropColumn(
                name: "GeneralBalance",
                schema: "balance",
                table: "balance_snapshots");

            migrationBuilder.DropColumn(
                name: "MroBudget",
                schema: "balance",
                table: "balance_snapshots");

            migrationBuilder.DropColumn(
                name: "OwnerContributionsMro",
                schema: "balance",
                table: "balance_snapshots");

            migrationBuilder.RenameColumn(
                name: "XofBudget",
                schema: "balance",
                table: "balance_snapshots",
                newName: "OwnerContributions");

            migrationBuilder.RenameColumn(
                name: "SavedProfits",
                schema: "balance",
                table: "balance_snapshots",
                newName: "SavedProfitsMro");

            migrationBuilder.RenameColumn(
                name: "Rate",
                schema: "balance",
                table: "balance_snapshots",
                newName: "Budget");

            migrationBuilder.AddColumn<Guid>(
                name: "AgencyId",
                schema: "balance",
                table: "capital_changes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                schema: "balance",
                table: "capital_changes",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<decimal>(
                name: "NetResult",
                schema: "balance",
                table: "balance_snapshots",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,10)",
                oldPrecision: 28,
                oldScale: 10,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "CapitalUsed",
                schema: "balance",
                table: "balance_snapshots",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,10)",
                oldPrecision: 28,
                oldScale: 10,
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AgencyId",
                schema: "balance",
                table: "balance_snapshots",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                schema: "balance",
                table: "balance_snapshots",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_capital_changes_AgencyId_SetAt",
                schema: "balance",
                table: "capital_changes",
                columns: new[] { "AgencyId", "SetAt" });

            migrationBuilder.CreateIndex(
                name: "IX_balance_snapshots_AgencyId_Date",
                schema: "balance",
                table: "balance_snapshots",
                columns: new[] { "AgencyId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_capital_changes_AgencyId_SetAt",
                schema: "balance",
                table: "capital_changes");

            migrationBuilder.DropIndex(
                name: "IX_balance_snapshots_AgencyId_Date",
                schema: "balance",
                table: "balance_snapshots");

            migrationBuilder.DropColumn(
                name: "AgencyId",
                schema: "balance",
                table: "capital_changes");

            migrationBuilder.DropColumn(
                name: "Currency",
                schema: "balance",
                table: "capital_changes");

            migrationBuilder.DropColumn(
                name: "AgencyId",
                schema: "balance",
                table: "balance_snapshots");

            migrationBuilder.DropColumn(
                name: "Currency",
                schema: "balance",
                table: "balance_snapshots");

            migrationBuilder.RenameColumn(
                name: "SavedProfitsMro",
                schema: "balance",
                table: "balance_snapshots",
                newName: "SavedProfits");

            migrationBuilder.RenameColumn(
                name: "OwnerContributions",
                schema: "balance",
                table: "balance_snapshots",
                newName: "XofBudget");

            migrationBuilder.RenameColumn(
                name: "Budget",
                schema: "balance",
                table: "balance_snapshots",
                newName: "Rate");

            migrationBuilder.AlterColumn<decimal>(
                name: "NetResult",
                schema: "balance",
                table: "balance_snapshots",
                type: "numeric(28,10)",
                precision: 28,
                scale: 10,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "CapitalUsed",
                schema: "balance",
                table: "balance_snapshots",
                type: "numeric(28,10)",
                precision: 28,
                scale: 10,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GeneralBalance",
                schema: "balance",
                table: "balance_snapshots",
                type: "numeric(28,10)",
                precision: 28,
                scale: 10,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MroBudget",
                schema: "balance",
                table: "balance_snapshots",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OwnerContributionsMro",
                schema: "balance",
                table: "balance_snapshots",
                type: "numeric(28,10)",
                precision: 28,
                scale: 10,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_capital_changes_SetAt",
                schema: "balance",
                table: "capital_changes",
                column: "SetAt");

            migrationBuilder.CreateIndex(
                name: "IX_balance_snapshots_Date",
                schema: "balance",
                table: "balance_snapshots",
                column: "Date",
                unique: true);
        }
    }
}
