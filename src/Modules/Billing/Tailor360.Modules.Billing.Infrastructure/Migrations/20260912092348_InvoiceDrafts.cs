using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invoices",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    discarded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    discarded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    discard_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    gst_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gstin = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    place_of_supply_state_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    price_list_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    calculation_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    scheme = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    supplier_state_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    tax_configuration_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_inclusive = table.Column<bool>(type: "boolean", nullable: false),
                    customer_address_line = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    customer_number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    customer_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    customer_locality = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    customer_postcode = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("pk_invoices", x => x.id);
                    table.CheckConstraint("ck_invoices_lifecycle_is_consistent", "(status = 2 AND discarded_at IS NOT NULL) OR (status <> 2 AND discarded_at IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "order_facts",
                schema: "billing",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    cancellation_reason_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_facts", x => x.order_id);
                });

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                schema: "billing",
                columns: table => new
                {
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    garment_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    line_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    item_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    catalogue_rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    applied_rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    discount_rule_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    discount_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    discount_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    tax_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    classification = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    tax_code_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    base_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    base_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    discount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    gross_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    gross_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    line_total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    line_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tax_total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    tax_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    taxable_value_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    taxable_value_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    variance_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    variance_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_lines", x => new { x.invoice_id, x.garment_job_id });
                    table.ForeignKey(
                        name: "fk_invoice_lines_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "billing",
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_fact_jobs",
                schema: "billing",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    garment_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    garment_job_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    job_index = table.Column<int>(type: "integer", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    cancellation_reason_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_fact_jobs", x => new { x.order_id, x.garment_job_id });
                    table.ForeignKey(
                        name: "fk_order_fact_jobs_order_facts_order_id",
                        column: x => x.order_id,
                        principalSchema: "billing",
                        principalTable: "order_facts",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invoice_line_surcharges",
                schema: "billing",
                columns: table => new
                {
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    garment_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    item_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    amount_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_line_surcharges", x => new { x.invoice_id, x.garment_job_id, x.position });
                    table.ForeignKey(
                        name: "fk_invoice_line_surcharges_invoice_lines",
                        columns: x => new { x.invoice_id, x.garment_job_id },
                        principalSchema: "billing",
                        principalTable: "invoice_lines",
                        principalColumns: new[] { "invoice_id", "garment_job_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invoice_tax_components",
                schema: "billing",
                columns: table => new
                {
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    garment_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    rate_percent = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    amount_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_tax_components", x => new { x.invoice_id, x.garment_job_id, x.kind });
                    table.ForeignKey(
                        name: "fk_invoice_tax_components_invoice_lines",
                        columns: x => new { x.invoice_id, x.garment_job_id },
                        principalSchema: "billing",
                        principalTable: "invoice_lines",
                        principalColumns: new[] { "invoice_id", "garment_job_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_organisation_branch_status_updated_at",
                schema: "billing",
                table: "invoices",
                columns: new[] { "organisation_id", "branch_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_organisation_order",
                schema: "billing",
                table: "invoices",
                columns: new[] { "organisation_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_order_facts_organisation_branch_status",
                schema: "billing",
                table: "order_facts",
                columns: new[] { "organisation_id", "branch_id", "status" });

            // What the model cannot express, each with the reason it is here.

            // 1. A garment job is charged on at most one live invoice — a draft or a posted one — across the
            // organisation. The rule is over a column the model does not map: `invoice_status` on the line
            // rows is the parent's status, denormalised by the parent's own trigger, because a partial unique
            // index cannot reach across the join. A discarded draft's lines leave the index, which is what
            // frees its jobs for another draft.
            migrationBuilder.Sql("""
                ALTER TABLE billing.invoice_lines
                ADD COLUMN invoice_status integer NOT NULL DEFAULT 0;
                """);

            migrationBuilder.Sql("""
                CREATE FUNCTION billing.invoice_lines_follow_status()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    UPDATE billing.invoice_lines
                       SET invoice_status = NEW.status
                     WHERE invoice_id = NEW.id;

                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER invoice_lines_follow_status
                AFTER UPDATE OF status ON billing.invoices
                FOR EACH ROW EXECUTE FUNCTION billing.invoice_lines_follow_status();
                """);

            // A line inserted while the parent is not a draft — which E09-F02-2's freeze will refuse anyway —
            // must carry the parent's status from the start, or the index would not see it.
            migrationBuilder.Sql("""
                CREATE FUNCTION billing.invoice_lines_take_parent_status()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    SELECT status INTO NEW.invoice_status FROM billing.invoices WHERE id = NEW.invoice_id;

                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER invoice_lines_take_parent_status
                BEFORE INSERT ON billing.invoice_lines
                FOR EACH ROW EXECUTE FUNCTION billing.invoice_lines_take_parent_status();
                """);

            // After the column it filters on, which is why it is not up with the other indexes.
            migrationBuilder.CreateIndex(
                name: "ux_invoice_lines_live_garment_job",
                schema: "billing",
                table: "invoice_lines",
                column: "garment_job_id",
                unique: true,
                filter: "invoice_status IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The triggers and the index go with their tables; the functions do not.
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS billing.invoice_lines_follow_status() CASCADE;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS billing.invoice_lines_take_parent_status() CASCADE;");

            migrationBuilder.DropTable(
                name: "invoice_line_surcharges",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "invoice_tax_components",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "order_fact_jobs",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "invoice_lines",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "order_facts",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "invoices",
                schema: "billing");
        }
    }
}
