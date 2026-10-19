using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ledger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ledger");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "ledger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SystemRole = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    AgencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "journal_entries",
                schema: "ledger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EntryType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceModule = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(219)", maxLength: 219, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReversesEntryId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_journal_entries_journal_entries_ReversesEntryId",
                        column: x => x.ReversesEntryId,
                        principalSchema: "ledger",
                        principalTable: "journal_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_lines",
                schema: "ledger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Side = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    EntryId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_lines", x => x.Id);
                    table.CheckConstraint("ck_journal_lines_amount_positive", "\"Amount\" > 0");
                    table.CheckConstraint("ck_journal_lines_side", "\"Side\" IN ('Debit', 'Credit')");
                    table.ForeignKey(
                        name: "FK_journal_lines_accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "ledger",
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_journal_lines_journal_entries_EntryId",
                        column: x => x.EntryId,
                        principalSchema: "ledger",
                        principalTable: "journal_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_AgencyId",
                schema: "ledger",
                table: "accounts",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_OwnerId_AgencyId",
                schema: "ledger",
                table: "accounts",
                columns: new[] { "OwnerId", "AgencyId" },
                unique: true,
                filter: "\"OwnerId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_SystemRole_AgencyId",
                schema: "ledger",
                table: "accounts",
                columns: new[] { "SystemRole", "AgencyId" },
                unique: true,
                filter: "\"SystemRole\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_ReversesEntryId",
                schema: "ledger",
                table: "journal_entries",
                column: "ReversesEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_SourceModule_SourceId",
                schema: "ledger",
                table: "journal_entries",
                columns: new[] { "SourceModule", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_journal_lines_AccountId",
                schema: "ledger",
                table: "journal_lines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_journal_lines_EntryId",
                schema: "ledger",
                table: "journal_lines",
                column: "EntryId");

            // --- Ajouté à la main : garanties en base que EF ne sait pas générer -------------------
            // Le code les respecte déjà ; la base les impose même à un INSERT / UPDATE écrit en SQL.

            // 1. Immuabilité (règle validée) : jamais d'UPDATE, de DELETE ni de TRUNCATE sur les écritures.
            //    Une erreur se corrige par une contre-passation. SQLSTATE 23001 (restrict_violation).
            migrationBuilder.Sql("""
                CREATE FUNCTION ledger.refuse_journal_modification() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Grand livre immuable : % interdit sur %. Contre-passer l''écriture.', TG_OP, TG_TABLE_NAME
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;

                CREATE TRIGGER trg_journal_entries_immutable
                    BEFORE UPDATE OR DELETE ON ledger.journal_entries
                    FOR EACH ROW EXECUTE FUNCTION ledger.refuse_journal_modification();

                CREATE TRIGGER trg_journal_entries_no_truncate
                    BEFORE TRUNCATE ON ledger.journal_entries
                    FOR EACH STATEMENT EXECUTE FUNCTION ledger.refuse_journal_modification();

                CREATE TRIGGER trg_journal_lines_immutable
                    BEFORE UPDATE OR DELETE ON ledger.journal_lines
                    FOR EACH ROW EXECUTE FUNCTION ledger.refuse_journal_modification();

                CREATE TRIGGER trg_journal_lines_no_truncate
                    BEFORE TRUNCATE ON ledger.journal_lines
                    FOR EACH STATEMENT EXECUTE FUNCTION ledger.refuse_journal_modification();
                """);

            // 2. Équilibre par devise, vérifié au COMMIT (déclencheur de contrainte différé) : toutes les lignes
            //    d'une écriture sont alors présentes. SQLSTATE 23514 (check_violation).
            migrationBuilder.Sql("""
                CREATE FUNCTION ledger.check_entry_balanced() RETURNS trigger
                LANGUAGE plpgsql AS $$
                DECLARE
                    unbalanced text;
                BEGIN
                    SELECT string_agg("Currency", ', ')
                    INTO unbalanced
                    FROM (
                        SELECT "Currency"
                        FROM ledger.journal_lines
                        WHERE "EntryId" = NEW."EntryId"
                        GROUP BY "Currency"
                        HAVING SUM(CASE WHEN "Side" = 'Debit' THEN "Amount" ELSE -"Amount" END) <> 0
                    ) AS by_currency;

                    IF unbalanced IS NOT NULL THEN
                        RAISE EXCEPTION 'Écriture % déséquilibrée en % : total des débits <> total des crédits.', NEW."EntryId", unbalanced
                            USING ERRCODE = 'check_violation';
                    END IF;

                    RETURN NULL;
                END;
                $$;

                CREATE CONSTRAINT TRIGGER trg_journal_lines_balanced
                    AFTER INSERT ON ledger.journal_lines
                    DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION ledger.check_entry_balanced();
                """);

            // 3. Au moins deux lignes par écriture, vérifié au COMMIT : sans lui, une écriture sans aucune ligne
            //    passerait, le contrôle d'équilibre ne se déclenchant que sur les lignes. SQLSTATE 23514.
            migrationBuilder.Sql("""
                CREATE FUNCTION ledger.check_entry_has_lines() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF (SELECT COUNT(*) FROM ledger.journal_lines WHERE "EntryId" = NEW."Id") < 2 THEN
                        RAISE EXCEPTION 'Écriture % : au moins deux lignes (un débit et un crédit).', NEW."Id"
                            USING ERRCODE = 'check_violation';
                    END IF;

                    RETURN NULL;
                END;
                $$;

                CREATE CONSTRAINT TRIGGER trg_journal_entries_has_lines
                    AFTER INSERT ON ledger.journal_entries
                    DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION ledger.check_entry_has_lines();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "journal_lines",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "journal_entries",
                schema: "ledger");

            // Les déclencheurs disparaissent avec leurs tables ; restent les fonctions ajoutées à la main.
            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS ledger.check_entry_has_lines();
                DROP FUNCTION IF EXISTS ledger.check_entry_balanced();
                DROP FUNCTION IF EXISTS ledger.refuse_journal_modification();
                """);
        }
    }
}
