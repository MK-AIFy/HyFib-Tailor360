using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialOrdersSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "orders");

            migrationBuilder.CreateTable(
                name: "estimates",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_draft_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estimate_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    issued_by = table.Column<Guid>(type: "uuid", nullable: true),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    superseded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    superseded_by_estimate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    converted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    converted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    converted_to_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    artefact_checksum = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    artefact_recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    totals_calculated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    totals_catalog_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    totals_price_list_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    totals_tax_configuration_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    totals_central_tax_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_central_tax_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_cess_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_cess_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_discount_total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_discount_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_grand_total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_grand_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_integrated_tax_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_integrated_tax_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_round_off_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_round_off_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_state_tax_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_state_tax_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_subtotal_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_subtotal_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_taxable_value_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_taxable_value_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_estimates", x => x.id);
                    table.CheckConstraint("ck_estimates_artefact_is_consistent", "(artefact_checksum IS NULL) = (artefact_recorded_at IS NULL)");
                    table.CheckConstraint("ck_estimates_converted_is_consistent", "(status = 'Converted') = (converted_at IS NOT NULL) AND (converted_at IS NULL) = (converted_to_order_id IS NULL)");
                    table.CheckConstraint("ck_estimates_price_amounts_not_negative", "totals_subtotal_amount >= 0 AND totals_discount_total_amount >= 0 AND totals_taxable_value_amount >= 0 AND totals_central_tax_amount >= 0 AND totals_state_tax_amount >= 0 AND totals_integrated_tax_amount >= 0 AND totals_cess_amount >= 0 AND totals_grand_total_amount >= 0");
                    table.CheckConstraint("ck_estimates_price_currency_is_shared", "totals_discount_total_currency = totals_subtotal_currency AND totals_taxable_value_currency = totals_subtotal_currency AND totals_central_tax_currency = totals_subtotal_currency AND totals_state_tax_currency = totals_subtotal_currency AND totals_integrated_tax_currency = totals_subtotal_currency AND totals_cess_currency = totals_subtotal_currency AND totals_round_off_currency = totals_subtotal_currency AND totals_grand_total_currency = totals_subtotal_currency");
                    table.CheckConstraint("ck_estimates_price_one_tax_scheme", "NOT (totals_integrated_tax_amount <> 0 AND (totals_central_tax_amount <> 0 OR totals_state_tax_amount <> 0))");
                    table.CheckConstraint("ck_estimates_superseded_by_is_not_self", "superseded_by_estimate_id IS NULL OR superseded_by_estimate_id <> id");
                    table.CheckConstraint("ck_estimates_superseded_is_consistent", "(status = 'Superseded') = (superseded_at IS NOT NULL) AND (superseded_at IS NULL) = (superseded_by_estimate_id IS NULL)");
                    table.CheckConstraint("ck_estimates_validity_not_before_issue", "valid_until >= issued_on");
                    table.ForeignKey(
                        name: "fk_estimates_estimates_superseded_by_estimate_id",
                        column: x => x.superseded_by_estimate_id,
                        principalSchema: "orders",
                        principalTable: "estimates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "orders",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handler_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => new { x.message_id, x.handler_name });
                });

            migrationBuilder.CreateTable(
                name: "order_drafts",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    started_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_drafts", x => x.id);
                    table.CheckConstraint("ck_order_drafts_expires_after_it_started", "expires_at > started_at");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    available_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    lease_owner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    dead_lettered_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    order_draft_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estimate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    production_started_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancellation_reason_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    totals_calculated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    totals_catalog_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    totals_price_list_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    totals_tax_configuration_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    totals_central_tax_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_central_tax_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_cess_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_cess_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_discount_total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_discount_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_grand_total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_grand_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_integrated_tax_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_integrated_tax_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_round_off_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_round_off_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_state_tax_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_state_tax_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_subtotal_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_subtotal_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_taxable_value_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_taxable_value_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.CheckConstraint("ck_orders_cancelled_is_consistent", "(status = 'Cancelled') = (cancelled_at IS NOT NULL) AND (cancelled_at IS NULL) = (cancellation_reason_code IS NULL) AND (cancelled_at IS NULL) = (cancellation_reason IS NULL)");
                    table.CheckConstraint("ck_orders_price_amounts_not_negative", "totals_subtotal_amount >= 0 AND totals_discount_total_amount >= 0 AND totals_taxable_value_amount >= 0 AND totals_central_tax_amount >= 0 AND totals_state_tax_amount >= 0 AND totals_integrated_tax_amount >= 0 AND totals_cess_amount >= 0 AND totals_grand_total_amount >= 0");
                    table.CheckConstraint("ck_orders_price_currency_is_shared", "totals_discount_total_currency = totals_subtotal_currency AND totals_taxable_value_currency = totals_subtotal_currency AND totals_central_tax_currency = totals_subtotal_currency AND totals_state_tax_currency = totals_subtotal_currency AND totals_integrated_tax_currency = totals_subtotal_currency AND totals_cess_currency = totals_subtotal_currency AND totals_round_off_currency = totals_subtotal_currency AND totals_grand_total_currency = totals_subtotal_currency");
                    table.CheckConstraint("ck_orders_price_one_tax_scheme", "NOT (totals_integrated_tax_amount <> 0 AND (totals_central_tax_amount <> 0 OR totals_state_tax_amount <> 0))");
                    table.CheckConstraint("ck_orders_revision_number_is_positive", "revision_number >= 1");
                    table.ForeignKey(
                        name: "fk_orders_estimates_estimate_id",
                        column: x => x.estimate_id,
                        principalSchema: "orders",
                        principalTable: "estimates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_draft_garments",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_draft_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    category_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    service_type_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    catalog_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    design_selection_draft_id = table.Column<Guid>(type: "uuid", nullable: true),
                    measurement_intent = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    measurement_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    measurement_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    instructions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reference_media_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_draft_garments", x => x.id);
                    table.CheckConstraint("ck_order_draft_garments_measurement_reuse", "(measurement_intent = 'ReuseVersion') = (measurement_version_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_order_draft_garments_order_drafts_order_draft_id",
                        column: x => x.order_draft_id,
                        principalSchema: "orders",
                        principalTable: "order_drafts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "garment_jobs",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_number = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    job_index = table.Column<int>(type: "integer", nullable: false),
                    category_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    service_type_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    workflow_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    production_started_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    production_started_by = table.Column<Guid>(type: "uuid", nullable: true),
                    hold_reason_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    hold_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    held_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    held_by = table.Column<Guid>(type: "uuid", nullable: true),
                    hold_approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    delivered_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancellation_reason_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_ready_for_delivery = table.Column<bool>(type: "boolean", nullable: false),
                    ready_state_computed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ready_state_blocks = table.Column<string>(type: "jsonb", nullable: false),
                    reference_media_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    price_calculated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    price_catalog_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_price_list_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_tax_configuration_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_central_tax_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    price_central_tax_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    price_cess_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    price_cess_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    price_discount_total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    price_discount_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    price_grand_total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    price_grand_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    price_integrated_tax_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    price_integrated_tax_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    price_round_off_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    price_round_off_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    price_state_tax_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    price_state_tax_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    price_subtotal_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    price_subtotal_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    price_taxable_value_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    price_taxable_value_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_garment_jobs", x => x.id);
                    table.CheckConstraint("ck_garment_jobs_cancelled_is_consistent", "(status = 'Cancelled') = (cancelled_at IS NOT NULL) AND (cancelled_at IS NULL) = (cancellation_reason_code IS NULL)");
                    table.CheckConstraint("ck_garment_jobs_delivered_is_consistent", "status <> 'Delivered' OR delivered_at IS NOT NULL");
                    table.CheckConstraint("ck_garment_jobs_hold_is_consistent", "(held_at IS NOT NULL) = (hold_reason_code IS NOT NULL) AND (held_at IS NULL) = (hold_reason IS NULL)");
                    table.CheckConstraint("ck_garment_jobs_job_index_is_positive", "job_index >= 1");
                    table.CheckConstraint("ck_garment_jobs_price_amounts_not_negative", "price_subtotal_amount >= 0 AND price_discount_total_amount >= 0 AND price_taxable_value_amount >= 0 AND price_central_tax_amount >= 0 AND price_state_tax_amount >= 0 AND price_integrated_tax_amount >= 0 AND price_cess_amount >= 0 AND price_grand_total_amount >= 0");
                    table.CheckConstraint("ck_garment_jobs_price_currency_is_shared", "price_discount_total_currency = price_subtotal_currency AND price_taxable_value_currency = price_subtotal_currency AND price_central_tax_currency = price_subtotal_currency AND price_state_tax_currency = price_subtotal_currency AND price_integrated_tax_currency = price_subtotal_currency AND price_cess_currency = price_subtotal_currency AND price_round_off_currency = price_subtotal_currency AND price_grand_total_currency = price_subtotal_currency");
                    table.CheckConstraint("ck_garment_jobs_price_one_tax_scheme", "NOT (price_integrated_tax_amount <> 0 AND (price_central_tax_amount <> 0 OR price_state_tax_amount <> 0))");
                    table.CheckConstraint("ck_garment_jobs_ready_has_been_computed", "is_ready_for_delivery = false OR ready_state_computed_at IS NOT NULL");
                    table.CheckConstraint("ck_garment_jobs_ready_has_no_blocks", "is_ready_for_delivery = false OR jsonb_array_length(ready_state_blocks) = 0");
                    table.CheckConstraint("ck_garment_jobs_workflow_pin_is_consistent", "(production_started_at IS NULL) = (workflow_version_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_garment_jobs_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_revisions",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    superseded_estimate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    totals_calculated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    totals_catalog_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    totals_price_list_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    totals_tax_configuration_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    totals_central_tax_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_central_tax_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_cess_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_cess_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_discount_total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_discount_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_grand_total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_grand_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_integrated_tax_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_integrated_tax_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_round_off_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_round_off_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_state_tax_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_state_tax_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_subtotal_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_subtotal_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    totals_taxable_value_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    totals_taxable_value_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_revisions", x => x.id);
                    table.CheckConstraint("ck_order_revisions_number_is_positive", "revision_number >= 1");
                    table.CheckConstraint("ck_order_revisions_price_amounts_not_negative", "totals_subtotal_amount >= 0 AND totals_discount_total_amount >= 0 AND totals_taxable_value_amount >= 0 AND totals_central_tax_amount >= 0 AND totals_state_tax_amount >= 0 AND totals_integrated_tax_amount >= 0 AND totals_cess_amount >= 0 AND totals_grand_total_amount >= 0");
                    table.CheckConstraint("ck_order_revisions_price_currency_is_shared", "totals_discount_total_currency = totals_subtotal_currency AND totals_taxable_value_currency = totals_subtotal_currency AND totals_central_tax_currency = totals_subtotal_currency AND totals_state_tax_currency = totals_subtotal_currency AND totals_integrated_tax_currency = totals_subtotal_currency AND totals_cess_currency = totals_subtotal_currency AND totals_round_off_currency = totals_subtotal_currency AND totals_grand_total_currency = totals_subtotal_currency");
                    table.CheckConstraint("ck_order_revisions_price_one_tax_scheme", "NOT (totals_integrated_tax_amount <> 0 AND (totals_central_tax_amount <> 0 OR totals_state_tax_amount <> 0))");
                    table.ForeignKey(
                        name: "fk_order_revisions_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_draft_garment_dependencies",
                schema: "orders",
                columns: table => new
                {
                    order_draft_garment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prerequisite_order_draft_garment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    declared_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    declared_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_draft_garment_dependencies", x => new { x.order_draft_garment_id, x.prerequisite_order_draft_garment_id, x.kind });
                    table.CheckConstraint("ck_order_draft_garment_dependencies_not_on_itself", "order_draft_garment_id <> prerequisite_order_draft_garment_id");
                    table.ForeignKey(
                        name: "fk_order_draft_garment_dependencies_order_draft_garments_order",
                        column: x => x.order_draft_garment_id,
                        principalSchema: "orders",
                        principalTable: "order_draft_garments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_order_draft_garment_dependencies_order_draft_garments_prere",
                        column: x => x.prerequisite_order_draft_garment_id,
                        principalSchema: "orders",
                        principalTable: "order_draft_garments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "design_snapshots",
                schema: "orders",
                columns: table => new
                {
                    garment_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    category_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    service_type_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    service_type_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    selections = table.Column<string>(type: "jsonb", nullable: false),
                    garment_instructions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    conditional_notes = table.Column<string>(type: "jsonb", nullable: false),
                    frozen_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_design_snapshots", x => x.garment_job_id);
                    table.ForeignKey(
                        name: "fk_design_snapshots_garment_jobs_id",
                        column: x => x.garment_job_id,
                        principalSchema: "orders",
                        principalTable: "garment_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_dependencies",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    garment_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prerequisite_garment_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    declared_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    declared_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_dependencies", x => x.id);
                    table.CheckConstraint("ck_job_dependencies_not_on_itself", "garment_job_id <> prerequisite_garment_job_id");
                    table.ForeignKey(
                        name: "fk_job_dependencies_garment_jobs_garment_job_id",
                        column: x => x.garment_job_id,
                        principalSchema: "orders",
                        principalTable: "garment_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_job_dependencies_garment_jobs_prerequisite_garment_job_id",
                        column: x => x.prerequisite_garment_job_id,
                        principalSchema: "orders",
                        principalTable: "garment_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateTable(
                name: "measurement_snapshots",
                schema: "orders",
                columns: table => new
                {
                    garment_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    measurement_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    measurement_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    taken_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    taken_by = table.Column<Guid>(type: "uuid", nullable: true),
                    values = table.Column<string>(type: "jsonb", nullable: false),
                    frozen_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_measurement_snapshots", x => x.garment_job_id);
                    table.CheckConstraint("ck_measurement_snapshots_version_number_is_positive", "version_number >= 1");
                    table.ForeignKey(
                        name: "fk_measurement_snapshots_garment_jobs_id",
                        column: x => x.garment_job_id,
                        principalSchema: "orders",
                        principalTable: "garment_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_estimates_customer",
                schema: "orders",
                table: "estimates",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_estimates_draft",
                schema: "orders",
                table: "estimates",
                columns: new[] { "organisation_id", "order_draft_id", "issued_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_estimates_superseded_by_estimate_id",
                schema: "orders",
                table: "estimates",
                column: "superseded_by_estimate_id");

            migrationBuilder.CreateIndex(
                name: "ux_estimates_organisation_number",
                schema: "orders",
                table: "estimates",
                columns: new[] { "organisation_id", "estimate_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_garment_jobs_due",
                schema: "orders",
                table: "garment_jobs",
                columns: new[] { "branch_id", "due_date" },
                filter: "status NOT IN ('Delivered', 'Closed', 'Cancelled')");

            migrationBuilder.CreateIndex(
                name: "ix_garment_jobs_ready",
                schema: "orders",
                table: "garment_jobs",
                column: "branch_id",
                filter: "is_ready_for_delivery");

            migrationBuilder.CreateIndex(
                name: "ux_garment_jobs_order_index",
                schema: "orders",
                table: "garment_jobs",
                columns: new[] { "order_id", "job_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_garment_jobs_organisation_number",
                schema: "orders",
                table: "garment_jobs",
                columns: new[] { "organisation_id", "job_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_dependencies_prerequisite",
                schema: "orders",
                table: "job_dependencies",
                column: "prerequisite_garment_job_id");

            migrationBuilder.CreateIndex(
                name: "ux_job_dependencies_job_prerequisite_kind",
                schema: "orders",
                table: "job_dependencies",
                columns: new[] { "garment_job_id", "prerequisite_garment_job_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_draft_garment_dependencies_prerequisite",
                schema: "orders",
                table: "order_draft_garment_dependencies",
                column: "prerequisite_order_draft_garment_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_draft_garments_unmeasured",
                schema: "orders",
                table: "order_draft_garments",
                column: "order_draft_id",
                filter: "measurement_version_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_order_draft_garments_draft_position",
                schema: "orders",
                table: "order_draft_garments",
                columns: new[] { "order_draft_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_drafts_branch_customer",
                schema: "orders",
                table: "order_drafts",
                columns: new[] { "branch_id", "customer_id" },
                filter: "consumed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_order_drafts_organisation_expiry",
                schema: "orders",
                table: "order_drafts",
                columns: new[] { "organisation_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ux_order_revisions_order_number",
                schema: "orders",
                table: "order_revisions",
                columns: new[] { "order_id", "revision_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer",
                schema: "orders",
                table: "orders",
                columns: new[] { "customer_id", "confirmed_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_orders_estimate",
                schema: "orders",
                table: "orders",
                column: "estimate_id",
                unique: true,
                filter: "estimate_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_orders_organisation_number",
                schema: "orders",
                table: "orders",
                columns: new[] { "organisation_id", "order_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "orders",
                table: "outbox_messages",
                columns: new[] { "available_at", "aggregate_id" },
                filter: "processed_at IS NULL AND dead_lettered_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at",
                schema: "orders",
                table: "outbox_messages",
                column: "processed_at");

            // ---------------------------------------------------------------------------------------------
            // Everything below is hand-written, because the model cannot express it. Each block says which
            // rule it holds and why the model could not.
            // ---------------------------------------------------------------------------------------------

            // INV-JOB-01. The copies frozen at confirmation are immutable, and a trigger is the only thing
            // that makes that true of the database rather than only of the domain type.
            //
            // THE 'Confirmed' ARM IS LOAD-BEARING AND MUST NOT BE SIMPLIFIED TO A BLANKET "NO UPDATE".
            // Order.Revise -> GarmentJob.Revise REPLACES all three copies, and INV-ORD-05 permits exactly
            // that while every job is still confirmed (GarmentJob.CheckRevision returns
            // RevisionRefusedAfterProduction otherwise). An append-only trigger would make Order.Revise fail
            // at the database. The shape - read the parent's status, allow the mutable state, refuse the
            // frozen one - is customers.template_fields_are_immutable.
            //
            // THE PARENT'S STATUS IS READ WITHOUT A LOCK, AND THAT IS DELIBERATE. Under READ COMMITTED it is a
            // check-then-act against a concurrent status change, and the honest way to close it in the trigger
            // would be SELECT ... FOR SHARE - which upgrades to FOR UPDATE when the same transaction then writes
            // garment_jobs, so two concurrent revisions of one order would deadlock rather than lose one to a
            // 409. The window is already closed for every write that goes through the Domain: Order.Revise
            // rewrites the job row as well (GarmentJob.Revise calls Touch), so garment_jobs.xmin turns a
            // concurrent status change into a concurrency conflict on the very statement this trigger would
            // otherwise have had to race. What is left uncovered is a writer that reaches the snapshot table
            // without touching the job, which is exactly the writer this trigger exists to catch and exactly the
            // one that has no business being there. Recorded rather than fixed, and a deadlock would be worse.
            migrationBuilder.Sql("""
                CREATE FUNCTION orders.job_snapshots_are_immutable()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    job_status text;
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        -- The arm says "when the job goes", and it now means it. An unconditional refusal
                        -- made the ON DELETE CASCADE this migration declares on both snapshot tables
                        -- unexecutable: PostgreSQL deletes the parent row first and then fires this BEFORE
                        -- DELETE trigger on each child, so a DELETE FROM orders.garment_jobs - and the
                        -- cascade from orders.orders one level above it - aborted with the very
                        -- restrict_violation whose own message said that case was allowed. The parent is
                        -- gone from this transaction's view by the time the cascade reaches here, so its
                        -- absence is exactly the test that tells the cascade from a delete that would
                        -- orphan the frozen copies while the job itself still stands.
                        IF EXISTS (SELECT 1 FROM orders.garment_jobs WHERE id = OLD.garment_job_id) THEN
                            RAISE EXCEPTION
                                'A garment job''s frozen copies go when the job goes and never on their '
                                'own (INV-JOB-01). A job that should not have been created is cancelled, '
                                'not deleted.'
                                USING ERRCODE = 'restrict_violation';
                        END IF;

                        RETURN OLD;
                    END IF;

                    SELECT status INTO job_status
                    FROM orders.garment_jobs
                    WHERE id = NEW.garment_job_id;

                    IF job_status = 'Confirmed' THEN
                        RETURN NEW;
                    END IF;

                    RAISE EXCEPTION
                        'The frozen copies of garment job % cannot change once it leaves Confirmed '
                        '(INV-JOB-01); it is %. Revise the order while every job is still confirmed, or '
                        'raise an alteration.', NEW.garment_job_id, COALESCE(job_status, 'gone')
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);

            // One function, two triggers. INSERT is deliberately absent from both: confirmation is what
            // writes them, and it writes them once.
            migrationBuilder.Sql("""
                CREATE TRIGGER measurement_snapshots_are_immutable
                BEFORE UPDATE OR DELETE ON orders.measurement_snapshots
                FOR EACH ROW EXECUTE FUNCTION orders.job_snapshots_are_immutable();
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER design_snapshots_are_immutable
                BEFORE UPDATE OR DELETE ON orders.design_snapshots
                FOR EACH ROW EXECUTE FUNCTION orders.job_snapshots_are_immutable();
                """);

            // The third frozen copy, and the reason it needs a trigger of its own. The price copy is a set of
            // columns on garment_jobs rather than a price_snapshots table, because Entity Framework Core 10
            // cannot nest a two-column value object inside an owned type (OrdersDbContext.ConfigurePriceSnapshot
            // says why). A table-wide immutability trigger would therefore refuse every ordinary update the job
            // receives, so this one compares only the priced columns. The column list is what makes that safe
            // now that the gate's three columns are on this table too: they are updated constantly and are not
            // in it.
            //
            // The columns are written out rather than compared with to_jsonb(NEW) wholesale, so that a
            // column added later has to be considered here rather than silently becoming frozen or free.
            // The reasoning is customers.customer_merges_no_rewrite's.
            //
            // WRITTEN OUT AS COMPARISONS AND NOT AS A NAME LIST. The list form built two whole-row
            // to_jsonb() documents of a sixty-column row and aggregated a filtered jsonb_each over each of
            // them, on every UPDATE of the most-written table in the schema: garment_jobs is touched by
            // every workflow, QC, hold, dependency and custody event, because each of them recomputes the
            // gate and each recomputation calls Touch(). An explicit IS DISTINCT FROM chain has exactly the
            // property the list was chosen for - a column added later is absent from it and has to be
            // considered - at a fraction of the cost.
            migrationBuilder.Sql("""
                CREATE FUNCTION orders.garment_job_price_is_immutable()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    -- Both sides, not OLD alone. Order.Revise is the only path that rewrites these
                    -- columns and it leaves the job Confirmed (GarmentJob.CheckRevision refuses any
                    -- other status and Revise never moves it), so nothing legitimate re-prices on the
                    -- same statement that moves the status. Reading OLD alone would let the
                    -- StartProduction update carry a new price out of Confirmed with it.
                    IF OLD.status = 'Confirmed' AND NEW.status = 'Confirmed' THEN
                        RETURN NEW;
                    END IF;

                    IF NEW.price_catalog_version_id IS DISTINCT FROM OLD.price_catalog_version_id
                       OR NEW.price_price_list_version_id
                           IS DISTINCT FROM OLD.price_price_list_version_id
                       OR NEW.price_tax_configuration_version_id
                           IS DISTINCT FROM OLD.price_tax_configuration_version_id
                       OR NEW.price_calculated_at IS DISTINCT FROM OLD.price_calculated_at
                       OR NEW.price_subtotal_amount IS DISTINCT FROM OLD.price_subtotal_amount
                       OR NEW.price_subtotal_currency IS DISTINCT FROM OLD.price_subtotal_currency
                       OR NEW.price_discount_total_amount IS DISTINCT FROM OLD.price_discount_total_amount
                       OR NEW.price_discount_total_currency
                           IS DISTINCT FROM OLD.price_discount_total_currency
                       OR NEW.price_taxable_value_amount IS DISTINCT FROM OLD.price_taxable_value_amount
                       OR NEW.price_taxable_value_currency
                           IS DISTINCT FROM OLD.price_taxable_value_currency
                       OR NEW.price_central_tax_amount IS DISTINCT FROM OLD.price_central_tax_amount
                       OR NEW.price_central_tax_currency IS DISTINCT FROM OLD.price_central_tax_currency
                       OR NEW.price_state_tax_amount IS DISTINCT FROM OLD.price_state_tax_amount
                       OR NEW.price_state_tax_currency IS DISTINCT FROM OLD.price_state_tax_currency
                       OR NEW.price_integrated_tax_amount IS DISTINCT FROM OLD.price_integrated_tax_amount
                       OR NEW.price_integrated_tax_currency
                           IS DISTINCT FROM OLD.price_integrated_tax_currency
                       OR NEW.price_cess_amount IS DISTINCT FROM OLD.price_cess_amount
                       OR NEW.price_cess_currency IS DISTINCT FROM OLD.price_cess_currency
                       OR NEW.price_round_off_amount IS DISTINCT FROM OLD.price_round_off_amount
                       OR NEW.price_round_off_currency IS DISTINCT FROM OLD.price_round_off_currency
                       OR NEW.price_grand_total_amount IS DISTINCT FROM OLD.price_grand_total_amount
                       OR NEW.price_grand_total_currency IS DISTINCT FROM OLD.price_grand_total_currency
                    THEN
                        RAISE EXCEPTION
                            'The price copy frozen onto garment job % cannot change once it leaves Confirmed '
                            '(INV-JOB-01); it is %. Revise the order while every job is still confirmed, or '
                            'raise an alteration.', OLD.id, NEW.status
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER garment_job_price_is_immutable
                BEFORE UPDATE ON orders.garment_jobs
                FOR EACH ROW EXECUTE FUNCTION orders.garment_job_price_is_immutable();
                """);

            // A revision appends, never rewrites. The pattern is customers.consent_records_append_only and
            // customers.measurement_versions_append_only, and platform.audit_events before them.
            //
            // APPEND-ONLY IS ABOUT THE ROW, NOT ABOUT THE ORDER IT BELONGS TO. An UPDATE is refused whatever
            // is happening around it. A DELETE is refused only while the order still stands: order_revisions
            // is declared ON DELETE CASCADE from orders.orders, and an unconditional refusal made that
            // cascade unexecutable - deleting an order aborted on a trigger whose own subject was the
            // revision row rather than the order. The parent is already gone from this transaction's view
            // when the cascade reaches here, which is the test.
            migrationBuilder.Sql("""
                CREATE FUNCTION orders.order_revisions_append_only()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF TG_OP = 'DELETE'
                       AND NOT EXISTS (SELECT 1 FROM orders.orders WHERE id = OLD.order_id) THEN
                        RETURN OLD;
                    END IF;

                    RAISE EXCEPTION
                        'orders.order_revisions is append-only: a revision appends and never rewrites, so % '
                        'is not permitted while the order stands. Record another revision.', TG_OP
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER order_revisions_append_only
                BEFORE UPDATE OR DELETE ON orders.order_revisions
                FOR EACH ROW EXECUTE FUNCTION orders.order_revisions_append_only();
                """);

            // Entity Framework has no fluent way to declare a foreign key DEFERRABLE, so the constraint it
            // emitted above is altered here. NO ACTION is checked at the end of the statement and, deferred,
            // at COMMIT; RESTRICT is checked immediately and cannot be deferred at all.
            //
            // Why it has to be deferred: deleting an order cascades into every garment job in one statement.
            // Where garment two declares garment one as its `finish_before` prerequisite, PostgreSQL evaluated
            // this key against garment one before garment two's own cascade had removed the row naming it, and
            // refused with 23503 - so an order carrying any declared dependency could not be deleted at all.
            // That was proved on CI by OrdersCascadeTests; no static reading of the schema settled it.
            //
            // Deferring moves the check to COMMIT, where the question is actually decidable: by then either
            // both rows have gone together, which is the whole-order cascade, or a prerequisite has been
            // deleted while its dependent survives - and that is still refused, which is the guarantee this
            // key exists for. Nothing else in this schema defers, so the two-phase behaviour is confined to
            // the one constraint that needs it.
            migrationBuilder.Sql("""
                ALTER TABLE orders.job_dependencies
                    ALTER CONSTRAINT fk_job_dependencies_garment_jobs_prerequisite_garment_job_id
                    DEFERRABLE INITIALLY DEFERRED;
                """);

            // INV-JOB-09 calls a dependency a promise the shop floor has been given. GarmentJob publishes no
            // withdraw and JobDependency no mutator; this is the database saying the same thing - while the
            // garment it was declared on is still there. The same reading as order_revisions_append_only
            // above: the row is declared ON DELETE CASCADE from orders.garment_jobs, and a promise about a
            // garment cannot outlive the garment. The prerequisite side is a different question answered by
            // a different mechanism - fk_job_dependencies_garment_jobs_prerequisite_garment_job_id is
            // ON DELETE NO ACTION and DEFERRABLE INITIALLY DEFERRED, so the garment somebody is waiting for
            // cannot be taken from under them - checked at COMMIT, which is the only point at which that
            // question is decidable while a whole order is being removed in one statement.
            migrationBuilder.Sql("""
                CREATE FUNCTION orders.job_dependencies_append_only()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF TG_OP = 'DELETE'
                       AND NOT EXISTS (
                           SELECT 1 FROM orders.garment_jobs WHERE id = OLD.garment_job_id) THEN
                        RETURN OLD;
                    END IF;

                    RAISE EXCEPTION
                        'orders.job_dependencies is append-only: a declared dependency is a promise the shop '
                        'floor has been given (INV-JOB-09), so % is not permitted while the garment it was '
                        'declared on stands.', TG_OP
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER job_dependencies_append_only
                BEFORE UPDATE OR DELETE ON orders.job_dependencies
                FOR EACH ROW EXECUTE FUNCTION orders.job_dependencies_append_only();
                """);

            // INV-JOB-07: no route makes a garment ready except the gate. A job is confirmed NOT ready and
            // only ever becomes ready by being moved there, so an insert arriving already ready would skip
            // the evaluation entirely - the shape customers.template_versions_are_immutable uses to refuse an
            // insert that arrives already published. The rest of the single-writer property stays where
            // invariants.md puts it, on the write path, because the four legitimate writers (the gate, Hold,
            // Resume and Cancel) are indistinguishable at the row level.
            //
            // ON garment_jobs RATHER THAN ON A job_ready_state TABLE, AND WITHOUT THE DELETE ARM IT USED TO
            // HAVE. The gate's outcome is three columns on garment_jobs because Entity Framework Core 10.0.11
            // cannot read an entity that is both split across two tables and holds a complex property, and
            // the frozen price copy is a complex property - OrdersDbContext.ConfigureReadyState records the
            // measurement. The insert arm crosses over unchanged, because a garment job inserted already
            // ready is the same defect whichever table the column is on. The delete arm does not: on the
            // fragment it said "the outcome row does not go without the job", and there is no such row any
            // more. The same trigger on garment_jobs would say something else and something stronger - that
            // a garment job can never be deleted at all - which no document states and which would refuse
            // the cascade from orders.orders.
            migrationBuilder.Sql("""
                CREATE FUNCTION orders.garment_jobs_ready_is_gate_only()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF NEW.is_ready_for_delivery THEN
                        RAISE EXCEPTION
                            'A garment job is confirmed not ready and becomes ready only by being moved '
                            'there by the ready gate (INV-JOB-07).'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER garment_jobs_ready_is_gate_only
                BEFORE INSERT ON orders.garment_jobs
                FOR EACH ROW EXECUTE FUNCTION orders.garment_jobs_ready_is_gate_only();
                """);

            // INV-ORD-03: a display number is never reused, and never rewritten either - the half that holds
            // when somebody reaches the table. The domain publishes no mutator for any of the three.
            //
            // THREE FUNCTIONS RATHER THAN ONE TAKING THE COLUMN NAME AS A TRIGGER ARGUMENT. The generic form
            // read its column through to_jsonb(NEW) ->> ... and to_jsonb(OLD) ->> ..., which serialises the
            // whole row twice to read one text column; on orders.garment_jobs that is a sixty-column row,
            // serialised on every update of the most-written table in the schema, beside the price trigger
            // above doing the same thing. A named column needs no serialisation at all, and the cost of
            // saying it three times is three three-line functions.
            migrationBuilder.Sql("""
                CREATE FUNCTION orders.order_number_is_immutable()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF NEW.order_number IS DISTINCT FROM OLD.order_number THEN
                        RAISE EXCEPTION
                            'A display number is never reused and never rewritten (INV-ORD-03), so '
                            'orders.orders.order_number cannot change.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER orders_number_is_immutable
                BEFORE UPDATE ON orders.orders
                FOR EACH ROW EXECUTE FUNCTION orders.order_number_is_immutable();
                """);

            migrationBuilder.Sql("""
                CREATE FUNCTION orders.estimate_number_is_immutable()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF NEW.estimate_number IS DISTINCT FROM OLD.estimate_number THEN
                        RAISE EXCEPTION
                            'A display number is never reused and never rewritten (INV-ORD-03), so '
                            'orders.estimates.estimate_number cannot change.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER estimates_number_is_immutable
                BEFORE UPDATE ON orders.estimates
                FOR EACH ROW EXECUTE FUNCTION orders.estimate_number_is_immutable();
                """);

            migrationBuilder.Sql("""
                CREATE FUNCTION orders.garment_job_number_is_immutable()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF NEW.job_number IS DISTINCT FROM OLD.job_number THEN
                        RAISE EXCEPTION
                            'A display number is never reused and never rewritten (INV-ORD-03), so '
                            'orders.garment_jobs.job_number cannot change.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER garment_jobs_number_is_immutable
                BEFORE UPDATE ON orders.garment_jobs
                FOR EACH ROW EXECUTE FUNCTION orders.garment_job_number_is_immutable();
                """);

            // Defaults for the three collection columns. Declared here rather than in the model
            // deliberately: Entity Framework treats a declared default as store-generated, and the value it
            // would then decline to write is the empty collection - which is the ordinary case for all three.
            // Every writer supplies a value; this is only what a repair script gets if it does not.
            migrationBuilder.Sql(
                "ALTER TABLE orders.garment_jobs "
                + "ALTER COLUMN ready_state_blocks SET DEFAULT '[]'::jsonb;");
            migrationBuilder.Sql(
                "ALTER TABLE orders.order_draft_garments "
                + "ALTER COLUMN reference_media_ids SET DEFAULT '{}'::uuid[];");
            migrationBuilder.Sql(
                "ALTER TABLE orders.garment_jobs "
                + "ALTER COLUMN reference_media_ids SET DEFAULT '{}'::uuid[];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Triggers before the tables they guard, and each function after the trigger that uses it:
            // dropping the table leaves the function behind, and a re-applied migration would then fail on
            // CREATE FUNCTION. The reverse order of Up.
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS garment_jobs_number_is_immutable ON orders.garment_jobs;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS orders.garment_job_number_is_immutable();");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS estimates_number_is_immutable ON orders.estimates;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS orders.estimate_number_is_immutable();");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS orders_number_is_immutable ON orders.orders;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS orders.order_number_is_immutable();");

            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS garment_jobs_ready_is_gate_only ON orders.garment_jobs;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS orders.garment_jobs_ready_is_gate_only();");

            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS job_dependencies_append_only ON orders.job_dependencies;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS orders.job_dependencies_append_only();");

            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS order_revisions_append_only ON orders.order_revisions;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS orders.order_revisions_append_only();");

            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS garment_job_price_is_immutable ON orders.garment_jobs;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS orders.garment_job_price_is_immutable();");

            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS design_snapshots_are_immutable ON orders.design_snapshots;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS measurement_snapshots_are_immutable "
                + "ON orders.measurement_snapshots;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS orders.job_snapshots_are_immutable();");

            // The hand-added index and the two hand-added check constraints go with their table below.

            migrationBuilder.DropTable(
                name: "design_snapshots",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "job_dependencies",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "measurement_snapshots",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_draft_garment_dependencies",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_revisions",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "garment_jobs",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_draft_garments",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_drafts",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "estimates",
                schema: "orders");
        }
    }
}
