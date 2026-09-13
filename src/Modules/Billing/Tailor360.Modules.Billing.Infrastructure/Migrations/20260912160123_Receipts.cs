using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Receipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "receipts",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    barcode_payload = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    financial_year = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    issued_by = table.Column<Guid>(type: "uuid", nullable: true),
                    allocated_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    allocated_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    order_outstanding_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    order_outstanding_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    unapplied_advance_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unapplied_advance_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipts", x => x.id);
                    table.CheckConstraint("ck_receipts_amount_is_whole", "amount_amount = allocated_amount + unapplied_advance_amount");
                    table.ForeignKey(
                        name: "fk_receipts_payments_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "billing",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_receipts_organisation_branch_issued_at",
                schema: "billing",
                table: "receipts",
                columns: new[] { "organisation_id", "branch_id", "issued_at" });

            migrationBuilder.CreateIndex(
                name: "ux_receipts_organisation_barcode_payload",
                schema: "billing",
                table: "receipts",
                columns: new[] { "organisation_id", "barcode_payload" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_receipts_organisation_receipt_number",
                schema: "billing",
                table: "receipts",
                columns: new[] { "organisation_id", "receipt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_receipts_payment",
                schema: "billing",
                table: "receipts",
                column: "payment_id",
                unique: true);

            // What the model cannot express, with its reason (G-4, INV-PAY-01): a receipt is written once with
            // its payment and never changed or deleted — its number is a gapless series and its figures are
            // what the customer was handed. The same append-only function the payments use.
            migrationBuilder.Sql("""
                CREATE TRIGGER receipts_are_append_only
                BEFORE UPDATE OR DELETE ON billing.receipts
                FOR EACH ROW EXECUTE FUNCTION billing.financial_records_are_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The trigger goes with its table; the function it calls belongs to InvoicePosting and stays.
            migrationBuilder.DropTable(
                name: "receipts",
                schema: "billing");
        }
    }
}
