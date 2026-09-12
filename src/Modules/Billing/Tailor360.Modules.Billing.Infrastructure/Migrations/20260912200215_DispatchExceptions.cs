using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DispatchExceptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dispatch_exceptions",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    reason_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    consumed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    expired_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    max_outstanding_amount_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    max_outstanding_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispatch_exceptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dispatch_exception_jobs",
                schema: "billing",
                columns: table => new
                {
                    dispatch_exception_id = table.Column<Guid>(type: "uuid", nullable: false),
                    garment_job_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispatch_exception_jobs", x => new { x.dispatch_exception_id, x.garment_job_id });
                    table.ForeignKey(
                        name: "fk_dispatch_exception_jobs_dispatch_exceptions_dispatch_except",
                        column: x => x.dispatch_exception_id,
                        principalSchema: "billing",
                        principalTable: "dispatch_exceptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dispatch_exceptions_organisation_order",
                schema: "billing",
                table: "dispatch_exceptions",
                columns: new[] { "organisation_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_dispatch_exceptions_status_expires_at",
                schema: "billing",
                table: "dispatch_exceptions",
                columns: new[] { "status", "expires_at" });

            // What the model cannot express, with its reason (G-4, plan lines 1908-1916).

            // A dispatch exception is approved once, then takes exactly one further write — its
            // consumption or its expiry, each moving only that transition's own columns — and is never
            // deleted: it is what a dispatch authorisation proves it relied on. The same one-transition
            // shape as `reconciliation_batches_are_the_one_transition()`, for the same reason: this table
            // is a single-use grant, and the trigger is what makes "single-use" true at the database
            // rather than only in application code that a repair script could bypass.
            migrationBuilder.Sql("""
                CREATE FUNCTION billing.dispatch_exceptions_are_the_one_transition()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        IF NEW.status <> 0 OR NEW.consumed_by IS NOT NULL OR NEW.consumed_at IS NOT NULL OR NEW.expired_at IS NOT NULL THEN
                            RAISE EXCEPTION
                                'A dispatch exception is approved once, as status 0 with no consumption or expiry recorded. '
                                'Status % is reached by consuming or expiring it, not by inserting it.', NEW.status
                                USING ERRCODE = 'restrict_violation';
                        END IF;

                        RETURN NEW;
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION
                            'A dispatch exception is never deleted. It is what a dispatch authorisation proves it relied on.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF OLD.status <> 0 THEN
                        RAISE EXCEPTION 'A consumed or expired dispatch exception is immutable.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF NEW.organisation_id = OLD.organisation_id
                       AND NEW.branch_id = OLD.branch_id
                       AND NEW.order_id = OLD.order_id
                       AND NEW.policy_version = OLD.policy_version
                       AND NEW.reason_code = OLD.reason_code
                       AND NEW.reason_text = OLD.reason_text
                       AND NEW.approved_by = OLD.approved_by
                       AND NEW.approved_at = OLD.approved_at
                       AND NEW.expires_at = OLD.expires_at
                       AND NEW.max_outstanding_amount_amount = OLD.max_outstanding_amount_amount
                       AND NEW.max_outstanding_amount_currency = OLD.max_outstanding_amount_currency
                       AND NEW.status = 1
                       AND NEW.consumed_by IS NOT NULL
                       AND NEW.consumed_at IS NOT NULL
                       AND NEW.expired_at IS NULL THEN
                        RETURN NEW;
                    END IF;

                    IF NEW.organisation_id = OLD.organisation_id
                       AND NEW.branch_id = OLD.branch_id
                       AND NEW.order_id = OLD.order_id
                       AND NEW.policy_version = OLD.policy_version
                       AND NEW.reason_code = OLD.reason_code
                       AND NEW.reason_text = OLD.reason_text
                       AND NEW.approved_by = OLD.approved_by
                       AND NEW.approved_at = OLD.approved_at
                       AND NEW.expires_at = OLD.expires_at
                       AND NEW.max_outstanding_amount_amount = OLD.max_outstanding_amount_amount
                       AND NEW.max_outstanding_amount_currency = OLD.max_outstanding_amount_currency
                       AND NEW.status = 2
                       AND NEW.consumed_by IS NULL
                       AND NEW.consumed_at IS NULL
                       AND NEW.expired_at IS NOT NULL THEN
                        RETURN NEW;
                    END IF;

                    RAISE EXCEPTION
                        'An approved dispatch exception takes one write: its consumption, moving only the '
                        'dispatcher and the time, or its expiry, moving only the time.'
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER dispatch_exceptions_are_the_one_transition
                BEFORE INSERT OR UPDATE OR DELETE ON billing.dispatch_exceptions
                FOR EACH ROW EXECUTE FUNCTION billing.dispatch_exceptions_are_the_one_transition();
                """);

            // The jobs it covers are written once, in the same transaction as the parent's own single
            // insert, and never touched again — not even by another insert. The shared
            // `financial_records_are_append_only()` only refuses UPDATE and DELETE, which would let a row
            // be added to an exception's job set through direct SQL at any later time, with no trigger to
            // stop it, whatever the parent's own status: the exact "a repair script can bypass
            // application code" gap the one-transition trigger above exists to close for the parent row.
            // This dedicated function closes it for the child rows too. A status check alone is not
            // enough — a second, illegitimate insert while the parent is still freshly Approved would
            // pass it — so instead it compares the parent row's own `xmin` against the current
            // transaction: true only while the parent was inserted by this same transaction, which is
            // exactly the window EF's own `SaveChanges` writes the whole batch of job rows in, and never
            // true again once that transaction has committed.
            migrationBuilder.Sql("""
                CREATE FUNCTION billing.dispatch_exception_jobs_are_written_once()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    parent_xmin xid;
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        SELECT xmin INTO parent_xmin FROM billing.dispatch_exceptions WHERE id = NEW.dispatch_exception_id;
                        IF parent_xmin IS NULL OR parent_xmin <> pg_current_xact_id()::xid THEN
                            RAISE EXCEPTION
                                'A dispatch exception''s job set is written once, in the same transaction as its own creation.'
                                USING ERRCODE = 'restrict_violation';
                        END IF;

                        RETURN NEW;
                    END IF;

                    RAISE EXCEPTION 'A dispatch exception''s job set is written once and never changes.'
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER dispatch_exception_jobs_are_written_once
                BEFORE INSERT OR UPDATE OR DELETE ON billing.dispatch_exception_jobs
                FOR EACH ROW EXECUTE FUNCTION billing.dispatch_exception_jobs_are_written_once();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The triggers go with their tables; the functions do not.
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS billing.dispatch_exception_jobs_are_written_once() CASCADE;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS billing.dispatch_exceptions_are_the_one_transition() CASCADE;");

            migrationBuilder.DropTable(
                name: "dispatch_exception_jobs",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "dispatch_exceptions",
                schema: "billing");
        }
    }
}
