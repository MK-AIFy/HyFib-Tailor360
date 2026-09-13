using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InvoicePosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_lifecycle_is_consistent",
                schema: "billing",
                table: "invoices");

            migrationBuilder.AddColumn<string>(
                name: "barcode_payload",
                schema: "billing",
                table: "invoices",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "financial_year",
                schema: "billing",
                table: "invoices",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "invoice_number",
                schema: "billing",
                table: "invoices",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "order_revision_number",
                schema: "billing",
                table: "invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "posted_at",
                schema: "billing",
                table: "invoices",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "posted_by",
                schema: "billing",
                table: "invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "posted_on",
                schema: "billing",
                table: "invoices",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "adjustment_notes",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    posted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    central_tax_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    central_tax_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    cess_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cess_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    discount_total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    discount_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    grand_total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    grand_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    integrated_tax_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    integrated_tax_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    round_off_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    round_off_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    state_tax_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    state_tax_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    subtotal_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    subtotal_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    taxable_value_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    taxable_value_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adjustment_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_adjustment_notes_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "billing",
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "adjustment_note_lines",
                schema: "billing",
                columns: table => new
                {
                    note_id = table.Column<Guid>(type: "uuid", nullable: false),
                    garment_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    line_total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    line_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tax_total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    tax_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    taxable_value_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    taxable_value_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adjustment_note_lines", x => new { x.note_id, x.garment_job_id });
                    table.ForeignKey(
                        name: "fk_adjustment_note_lines_adjustment_notes_note_id",
                        column: x => x.note_id,
                        principalSchema: "billing",
                        principalTable: "adjustment_notes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invoice_cancellations",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_note_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_cancellations", x => x.id);
                    table.ForeignKey(
                        name: "fk_invoice_cancellations_adjustment_notes",
                        column: x => x.credit_note_id,
                        principalSchema: "billing",
                        principalTable: "adjustment_notes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_cancellations_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "billing",
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "adjustment_note_taxes",
                schema: "billing",
                columns: table => new
                {
                    note_id = table.Column<Guid>(type: "uuid", nullable: false),
                    garment_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    rate_percent = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    amount_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adjustment_note_taxes", x => new { x.note_id, x.garment_job_id, x.kind });
                    table.ForeignKey(
                        name: "fk_adjustment_note_taxes_adjustment_note_lines",
                        columns: x => new { x.note_id, x.garment_job_id },
                        principalSchema: "billing",
                        principalTable: "adjustment_note_lines",
                        principalColumns: new[] { "note_id", "garment_job_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_invoices_organisation_barcode_payload",
                schema: "billing",
                table: "invoices",
                columns: new[] { "organisation_id", "barcode_payload" },
                unique: true,
                filter: "barcode_payload IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_invoices_organisation_invoice_number",
                schema: "billing",
                table: "invoices",
                columns: new[] { "organisation_id", "invoice_number" },
                unique: true,
                filter: "invoice_number IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_lifecycle_is_consistent",
                schema: "billing",
                table: "invoices",
                sql: "((status = 2 AND discarded_at IS NOT NULL) OR (status <> 2 AND discarded_at IS NULL))\nAND ((status = 1 AND invoice_number IS NOT NULL AND barcode_payload IS NOT NULL AND posted_at IS NOT NULL)\n     OR (status <> 1 AND invoice_number IS NULL AND barcode_payload IS NULL AND posted_at IS NULL))");

            migrationBuilder.CreateIndex(
                name: "ix_adjustment_notes_invoice",
                schema: "billing",
                table: "adjustment_notes",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ux_adjustment_notes_organisation_number",
                schema: "billing",
                table: "adjustment_notes",
                columns: new[] { "organisation_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoice_cancellations_credit_note_id",
                schema: "billing",
                table: "invoice_cancellations",
                column: "credit_note_id");

            migrationBuilder.CreateIndex(
                name: "ux_invoice_cancellations_invoice",
                schema: "billing",
                table: "invoice_cancellations",
                column: "invoice_id",
                unique: true);

            // Every draft made before this column existed was made against the order as Billing then knew it;
            // stamping it with the order's current revision keeps such a draft postable, exactly as a draft
            // made after this migration and not revised since would be. A draft whose order has no fact left
            // keeps zero and is refused at posting, which is the honest answer for it.
            migrationBuilder.Sql("""
                UPDATE billing.invoices AS invoices
                   SET order_revision_number = facts.revision_number
                  FROM billing.order_facts AS facts
                 WHERE facts.order_id = invoices.order_id
                   AND invoices.status = 0;
                """);

            // What the model cannot express, each with the reason it is here (G-4: financial immutability).

            // 1. A posted invoice never changes and is never deleted; a draft moves to posted or discarded and
            // nowhere else; nothing is inserted already posted. A cancellation is an appended row in its own
            // table, which is why no column of the invoice is permitted to move after posting — not even the
            // last-touched pair. The same shape as the price-list triggers.
            migrationBuilder.Sql("""
                CREATE FUNCTION billing.posted_invoices_are_immutable()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        IF NEW.status = 0 THEN
                            RETURN NEW;
                        END IF;

                        RAISE EXCEPTION
                            'An invoice is created as a draft. Status % is reached by posting or discarding it, '
                            'not by inserting it.', NEW.status
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        IF OLD.status = 0 THEN
                            RETURN OLD;
                        END IF;

                        RAISE EXCEPTION
                            'A posted or discarded invoice cannot be deleted. Payments, dispatch authorisations '
                            'and tax reports refer to it.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF OLD.status = 0 THEN
                        IF NEW.status IN (0, 1, 2) THEN
                            RETURN NEW;
                        END IF;

                        RAISE EXCEPTION
                            'A draft invoice is posted or discarded; status % is neither.', NEW.status
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF to_jsonb(NEW) IS DISTINCT FROM to_jsonb(OLD) THEN
                        RAISE EXCEPTION
                            'A posted invoice is immutable. It is cancelled by its compensating record and '
                            'corrected by a credit or debit note, never edited.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER posted_invoices_are_immutable
                BEFORE INSERT OR UPDATE OR DELETE ON billing.invoices
                FOR EACH ROW EXECUTE FUNCTION billing.posted_invoices_are_immutable();
                """);

            // 2. The lines, surcharges and tax components of an invoice that is no longer a draft: nothing is
            // inserted, changed or deleted. One function serves the three tables because each carries the
            // invoice's identifier. The invoice's own status trigger (E09-F02-1) writes the lines' denormalised
            // status when the invoice is posted; that write comes from the database, not the application
            // role, and is the one change the rows of a posted invoice are allowed.
            migrationBuilder.Sql("""
                CREATE FUNCTION billing.posted_invoice_rows_are_immutable()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    old_status integer;
                    new_status integer;
                BEGIN
                    IF pg_trigger_depth() > 1 THEN
                        RETURN COALESCE(NEW, OLD);
                    END IF;

                    IF TG_OP <> 'INSERT' THEN
                        SELECT status INTO old_status FROM billing.invoices WHERE id = OLD.invoice_id;
                    END IF;

                    IF TG_OP <> 'DELETE' THEN
                        SELECT status INTO new_status FROM billing.invoices WHERE id = NEW.invoice_id;
                    END IF;

                    IF COALESCE(old_status, 0) = 0 AND COALESCE(new_status, 0) = 0 THEN
                        RETURN COALESCE(NEW, OLD);
                    END IF;

                    RAISE EXCEPTION
                        'billing.% belongs to a posted or discarded invoice and cannot change. A posted invoice '
                        'is corrected by a credit or debit note.',
                        TG_TABLE_NAME
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);

            foreach (var table in new[] { "invoice_lines", "invoice_line_surcharges", "invoice_tax_components" })
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER {table}_follow_posting
                    BEFORE INSERT OR UPDATE OR DELETE ON billing.{table}
                    FOR EACH ROW EXECUTE FUNCTION billing.posted_invoice_rows_are_immutable();
                    """);
            }

            // 3. Cancellations and notes are appended, never changed and never deleted: they are the financial
            // record a cancelled or corrected invoice's displayed status derives from.
            migrationBuilder.Sql("""
                CREATE FUNCTION billing.financial_records_are_append_only()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION
                        'billing.% is append-only: a posted record is never changed or deleted.',
                        TG_TABLE_NAME
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);

            foreach (var table in new[] { "invoice_cancellations", "adjustment_notes", "adjustment_note_lines", "adjustment_note_taxes" })
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER {table}_are_append_only
                    BEFORE UPDATE OR DELETE ON billing.{table}
                    FOR EACH ROW EXECUTE FUNCTION billing.financial_records_are_append_only();
                    """);
            }

            // 4. A cancellation frees the invoice's garment jobs for another invoice: the lines' denormalised
            // status moves to 3 — cancelled, a value the invoice's own status column never takes — which
            // takes them out of the one-live-invoice-per-job index (E09-F02-1: WHERE invoice_status IN (0, 1)).
            // Written by the database at trigger depth two, which is the one write the rows of a posted
            // invoice permit.
            migrationBuilder.Sql("""
                CREATE FUNCTION billing.invoice_cancellations_release_jobs()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    UPDATE billing.invoice_lines
                       SET invoice_status = 3
                     WHERE invoice_id = NEW.invoice_id;

                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER invoice_cancellations_release_jobs
                AFTER INSERT ON billing.invoice_cancellations
                FOR EACH ROW EXECUTE FUNCTION billing.invoice_cancellations_release_jobs();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The triggers go with their tables and columns; the functions do not.
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS billing.posted_invoices_are_immutable() CASCADE;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS billing.posted_invoice_rows_are_immutable() CASCADE;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS billing.financial_records_are_append_only() CASCADE;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS billing.invoice_cancellations_release_jobs() CASCADE;");

            migrationBuilder.DropTable(
                name: "adjustment_note_taxes",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "invoice_cancellations",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "adjustment_note_lines",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "adjustment_notes",
                schema: "billing");

            migrationBuilder.DropIndex(
                name: "ux_invoices_organisation_barcode_payload",
                schema: "billing",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "ux_invoices_organisation_invoice_number",
                schema: "billing",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_lifecycle_is_consistent",
                schema: "billing",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "barcode_payload",
                schema: "billing",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "financial_year",
                schema: "billing",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "invoice_number",
                schema: "billing",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "order_revision_number",
                schema: "billing",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "posted_at",
                schema: "billing",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "posted_by",
                schema: "billing",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "posted_on",
                schema: "billing",
                table: "invoices");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_lifecycle_is_consistent",
                schema: "billing",
                table: "invoices",
                sql: "(status = 2 AND discarded_at IS NOT NULL) OR (status <> 2 AND discarded_at IS NULL)");
        }
    }
}
