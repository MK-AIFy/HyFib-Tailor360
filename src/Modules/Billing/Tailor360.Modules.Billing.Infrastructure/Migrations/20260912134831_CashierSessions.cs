using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CashierSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cashier_sessions",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cashier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    variance_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    counted_total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    counted_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    expected_total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    expected_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    opening_float_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    opening_float_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    variance_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    variance_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cashier_sessions", x => x.id);
                    table.CheckConstraint("ck_cashier_sessions_lifecycle_is_consistent", "(status = 0 AND closed_at IS NULL AND closed_by IS NULL) OR (status = 1 AND closed_at IS NOT NULL AND closed_by IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "payment_modes",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    requires_reference = table.Column<bool>(type: "boolean", nullable: false),
                    requires_provider = table.Column<bool>(type: "boolean", nullable: false),
                    allowed_for_refund = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_modes", x => x.id);
                    table.CheckConstraint("ck_payment_modes_code_is_well_formed", "code ~ '^[A-Z][A-Z0-9_]+$'");
                });

            migrationBuilder.CreateTable(
                name: "cashier_session_counts",
                schema: "billing",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    denomination = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cashier_session_counts", x => new { x.session_id, x.denomination });
                    table.ForeignKey(
                        name: "fk_cashier_session_counts_cashier_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "billing",
                        principalTable: "cashier_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cashier_session_mode_totals",
                schema: "billing",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    expected = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    counted = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    variance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cashier_session_mode_totals", x => new { x.session_id, x.mode_code });
                    table.ForeignKey(
                        name: "fk_cashier_session_mode_totals_cashier_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "billing",
                        principalTable: "cashier_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_mode_branches",
                schema: "billing",
                columns: table => new
                {
                    payment_mode_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_mode_branches", x => new { x.payment_mode_id, x.branch_id });
                    table.ForeignKey(
                        name: "fk_payment_mode_branches_payment_modes_payment_mode_id",
                        column: x => x.payment_mode_id,
                        principalSchema: "billing",
                        principalTable: "payment_modes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cashier_sessions_organisation_branch_opened_at",
                schema: "billing",
                table: "cashier_sessions",
                columns: new[] { "organisation_id", "branch_id", "opened_at" });

            migrationBuilder.CreateIndex(
                name: "ux_cashier_sessions_one_open",
                schema: "billing",
                table: "cashier_sessions",
                columns: new[] { "branch_id", "cashier_id" },
                unique: true,
                filter: "closed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_payment_modes_organisation_code",
                schema: "billing",
                table: "payment_modes",
                columns: new[] { "organisation_id", "code" },
                unique: true);

            // What the model cannot express, each with its reason (G-4, INV-CSH-05).

            // 1. A cashier session is created open and takes exactly one write: its close, which moves the
            // status and the close's own columns and nothing it was opened with. Once closed nothing on it
            // moves — a correction is a new adjusting record, and where money moved, a compensating payment —
            // and a session is never deleted: it is what a day's takings reconcile to.
            migrationBuilder.Sql("""
                CREATE FUNCTION billing.cashier_sessions_are_immutable()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        IF NEW.status = 0 THEN
                            RETURN NEW;
                        END IF;

                        RAISE EXCEPTION
                            'A cashier session is opened, then closed by its count. Status % is reached by '
                            'closing it, not by inserting it.', NEW.status
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION
                            'A cashier session is never deleted. The day''s takings reconcile to it.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF OLD.status = 0 THEN
                        IF NEW.status = 1
                           AND NEW.organisation_id = OLD.organisation_id
                           AND NEW.branch_id = OLD.branch_id
                           AND NEW.cashier_id = OLD.cashier_id
                           AND NEW.opened_at = OLD.opened_at
                           AND NEW.opening_float_amount = OLD.opening_float_amount
                           AND NEW.opening_float_currency = OLD.opening_float_currency THEN
                            RETURN NEW;
                        END IF;

                        RAISE EXCEPTION
                            'An open cashier session takes one write: its close. Its branch, its cashier, its '
                            'float and its opening do not move.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    RAISE EXCEPTION
                        'A closed cashier session is immutable. A correction is a new adjusting record.'
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER cashier_sessions_are_immutable
                BEFORE INSERT OR UPDATE OR DELETE ON billing.cashier_sessions
                FOR EACH ROW EXECUTE FUNCTION billing.cashier_sessions_are_immutable();
                """);

            // 2. The count sheet and the totals by mode are written once, at the close, and never changed or
            // deleted: the same append-only function the cancellations and the notes use (E09-F02-2).
            foreach (var table in new[] { "cashier_session_counts", "cashier_session_mode_totals" })
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER {table}_are_append_only
                    BEFORE UPDATE OR DELETE ON billing.{table}
                    FOR EACH ROW EXECUTE FUNCTION billing.financial_records_are_append_only();
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The triggers go with their tables; the function does not.
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS billing.cashier_sessions_are_immutable() CASCADE;");

            migrationBuilder.DropTable(
                name: "cashier_session_counts",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "cashier_session_mode_totals",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "payment_mode_branches",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "cashier_sessions",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "payment_modes",
                schema: "billing");
        }
    }
}
