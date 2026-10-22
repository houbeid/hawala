using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "audit_log",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AgencyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Changes = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_ActorId",
                schema: "audit",
                table: "audit_log",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_EntityType_EntityId",
                schema: "audit",
                table: "audit_log",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_OccurredAt",
                schema: "audit",
                table: "audit_log",
                column: "OccurredAt");

            // --- Ajouté à la main : garantie en base que EF ne sait pas générer -------------------
            // Journal en insertion seule : jamais d'UPDATE, de DELETE ni de TRUNCATE, même en SQL écrit à la main.
            // SQLSTATE 23001 (restrict_violation), comme le grand livre. Si la migration est régénérée, remettre ce bloc.
            migrationBuilder.Sql("""
                CREATE FUNCTION audit.refuse_audit_modification() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Journal d''audit en insertion seule : % interdit sur %.', TG_OP, TG_TABLE_NAME
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;

                CREATE TRIGGER trg_audit_log_immutable
                    BEFORE UPDATE OR DELETE ON audit.audit_log
                    FOR EACH ROW EXECUTE FUNCTION audit.refuse_audit_modification();

                CREATE TRIGGER trg_audit_log_no_truncate
                    BEFORE TRUNCATE ON audit.audit_log
                    FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_audit_modification();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "audit");

            // Les déclencheurs disparaissent avec la table ; reste la fonction ajoutée à la main.
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit.refuse_audit_modification();");
        }
    }
}
