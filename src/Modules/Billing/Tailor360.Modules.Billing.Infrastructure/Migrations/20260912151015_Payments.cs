using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Payments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payments",
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
                    mode_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    client_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    amount_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amount_is_positive", "amount_amount > 0");
                    table.ForeignKey(
                        name: "fk_payments_cashier_sessions_cashier_session_id",
                        column: x => x.cashier_session_id,
                        principalSchema: "billing",
                        principalTable: "cashier_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "advances",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    amount_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_advances", x => x.id);
                    table.CheckConstraint("ck_advances_amount_is_positive", "amount_amount > 0");
                    table.ForeignKey(
                        name: "fk_advances_payments_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "billing",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_allocations",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    advance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    allocated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    allocated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    amount_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_allocations", x => x.id);
                    table.CheckConstraint("ck_payment_allocations_amount_is_positive", "amount_amount > 0");
                    table.ForeignKey(
                        name: "fk_payment_allocations_advances_advance_id",
                        column: x => x.advance_id,
                        principalSchema: "billing",
                        principalTable: "advances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_allocations_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "billing",
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_allocations_payments_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "billing",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_advances_payment",
                schema: "billing",
                table: "advances",
                column: "payment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_advance",
                schema: "billing",
                table: "payment_allocations",
                column: "advance_id",
                filter: "advance_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_invoice",
                schema: "billing",
                table: "payment_allocations",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_payment_id",
                schema: "billing",
                table: "payment_allocations",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_organisation_order_recorded_at",
                schema: "billing",
                table: "payments",
                columns: new[] { "organisation_id", "order_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_session_mode",
                schema: "billing",
                table: "payments",
                columns: new[] { "cashier_session_id", "mode_code" });

            migrationBuilder.CreateIndex(
                name: "ux_payments_organisation_cashier_client_key",
                schema: "billing",
                table: "payments",
                columns: new[] { "organisation_id", "cashier_id", "client_key" },
                unique: true,
                filter: "client_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_payments_organisation_mode_reference",
                schema: "billing",
                table: "payments",
                columns: new[] { "organisation_id", "mode_code", "reference" },
                unique: true,
                filter: "reference IS NOT NULL");

            // What the model cannot express, with its reason (G-4, INV-PAY-01). A payment, an allocation and
            // an advance are written once and never changed or deleted: a status change is a new row, a
            // mistake is a compensating record (E09-F03-3), and what an advance still holds is the amount
            // minus the allocations that name it, never a column that moves. The same append-only function
            // the notes, the cancellations and the count sheets use.
            foreach (var table in new[] { "payments", "payment_allocations", "advances" })
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
                name: "payment_allocations",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "advances",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "billing");
        }
    }
}
