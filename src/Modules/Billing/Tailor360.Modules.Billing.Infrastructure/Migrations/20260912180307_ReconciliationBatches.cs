using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReconciliationBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reconciliation_batches",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cashier_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    close_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    approval_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    expected_total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    expected_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    recorded_total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    recorded_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    variance_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    variance_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reconciliation_batches", x => x.id);
                    table.ForeignKey(
                        name: "fk_reconciliation_batches_cashier_sessions_cashier_session_id",
                        column: x => x.cashier_session_id,
                        principalSchema: "billing",
                        principalTable: "cashier_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reconciliation_batch_mode_lines",
                schema: "billing",
                columns: table => new
                {
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    expected = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    recorded = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    variance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reconciliation_batch_mode_lines", x => new { x.batch_id, x.mode_code });
                    table.ForeignKey(
                        name: "fk_reconciliation_batch_mode_lines_reconciliation_batches_batc",
                        column: x => x.batch_id,
                        principalSchema: "billing",
                        principalTable: "reconciliation_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_reconciliation_batches_cashier_session",
                schema: "billing",
                table: "reconciliation_batches",
                column: "cashier_session_id",
                unique: true);

            // What the model cannot express, with its reason (G-4, INV-CSH-06).

            // A batch is opened once, at the session's close, as Pending or NotRequired; it takes exactly
            // one further write — its approval, moving only the approver, the time and the reason — and
            // is never deleted: it is what the session's close is reconciled against. The shape mirrors
            // `cashier_sessions_are_immutable` for the same reason: one row, one later transition.
            migrationBuilder.Sql("""
                CREATE FUNCTION billing.reconciliation_batches_are_the_one_transition()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        IF NEW.status = 2 THEN
                            RAISE EXCEPTION
                                'A reconciliation batch is opened pending or not required, then approved. '
                                'Status % is reached by approving it, not by inserting it.', NEW.status
                                USING ERRCODE = 'restrict_violation';
                        END IF;

                        RETURN NEW;
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION
                            'A reconciliation batch is never deleted. It is what the session''s close is reconciled against.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF OLD.status = 2 THEN
                        RAISE EXCEPTION 'An approved reconciliation batch is immutable.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF OLD.status = 0 THEN
                        RAISE EXCEPTION
                            'A batch with no variance beyond the threshold needs no approval and takes none.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF NEW.status = 2
                       AND NEW.organisation_id = OLD.organisation_id
                       AND NEW.branch_id = OLD.branch_id
                       AND NEW.cashier_session_id = OLD.cashier_session_id
                       AND NEW.type = OLD.type
                       AND NEW.closed_by = OLD.closed_by
                       AND NEW.close_reason IS NOT DISTINCT FROM OLD.close_reason
                       AND NEW.created_at = OLD.created_at
                       AND NEW.expected_total_amount = OLD.expected_total_amount
                       AND NEW.expected_total_currency = OLD.expected_total_currency
                       AND NEW.recorded_total_amount = OLD.recorded_total_amount
                       AND NEW.recorded_total_currency = OLD.recorded_total_currency
                       AND NEW.variance_amount = OLD.variance_amount
                       AND NEW.variance_currency = OLD.variance_currency
                       AND NEW.approved_by IS NOT NULL
                       AND NEW.approved_at IS NOT NULL
                       AND NEW.approval_reason IS NOT NULL THEN
                        RETURN NEW;
                    END IF;

                    RAISE EXCEPTION
                        'A pending reconciliation batch takes one write: its approval, which moves only the '
                        'approver, the time and the reason.'
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER reconciliation_batches_are_the_one_transition
                BEFORE INSERT OR UPDATE OR DELETE ON billing.reconciliation_batches
                FOR EACH ROW EXECUTE FUNCTION billing.reconciliation_batches_are_the_one_transition();
                """);

            // The mode lines are written once, at open, and never changed or deleted: the same
            // append-only function the cashier session's own count sheet and mode totals use.
            migrationBuilder.Sql("""
                CREATE TRIGGER reconciliation_batch_mode_lines_are_append_only
                BEFORE UPDATE OR DELETE ON billing.reconciliation_batch_mode_lines
                FOR EACH ROW EXECUTE FUNCTION billing.financial_records_are_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The triggers go with their tables; the function does not.
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS billing.reconciliation_batches_are_the_one_transition() CASCADE;");

            migrationBuilder.DropTable(
                name: "reconciliation_batch_mode_lines",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "reconciliation_batches",
                schema: "billing");
        }
    }
}
