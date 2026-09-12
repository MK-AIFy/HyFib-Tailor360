using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Reversals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_reversals",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_reversals", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_reversals_payments_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "billing",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refunds",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cashier_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cashier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mode_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    client_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    amount_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refunds", x => x.id);
                    table.CheckConstraint("ck_refunds_amount_is_positive", "amount_amount > 0");
                    table.CheckConstraint("ck_refunds_source_names_one", "(source = 0 AND payment_id IS NOT NULL AND invoice_id IS NULL) OR (source = 1 AND invoice_id IS NOT NULL AND payment_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_refunds_cashier_sessions_cashier_session_id",
                        column: x => x.cashier_session_id,
                        principalSchema: "billing",
                        principalTable: "cashier_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_refunds_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "billing",
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_refunds_payments_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "billing",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_payment_reversals_payment",
                schema: "billing",
                table: "payment_reversals",
                column: "payment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refunds_invoice",
                schema: "billing",
                table: "refunds",
                column: "invoice_id",
                filter: "invoice_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_organisation_order_recorded_at",
                schema: "billing",
                table: "refunds",
                columns: new[] { "organisation_id", "order_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_refunds_payment_id",
                schema: "billing",
                table: "refunds",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_session_mode",
                schema: "billing",
                table: "refunds",
                columns: new[] { "cashier_session_id", "mode_code" });

            migrationBuilder.CreateIndex(
                name: "ux_refunds_organisation_cashier_client_key",
                schema: "billing",
                table: "refunds",
                columns: new[] { "organisation_id", "cashier_id", "client_key" },
                unique: true,
                filter: "client_key IS NOT NULL");

            // What the model cannot express, with its reason (G-4, INV-PAY-01): a reversal and a refund are the
            // compensating records — written once, never changed or deleted; a mistake in one is another
            // record. The same append-only function the payments use.
            foreach (var table in new[] { "payment_reversals", "refunds" })
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
            // The triggers go with their tables; the function they call belongs to InvoicePosting and stays.
            migrationBuilder.DropTable(
                name: "payment_reversals",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "refunds",
                schema: "billing");
        }
    }
}
