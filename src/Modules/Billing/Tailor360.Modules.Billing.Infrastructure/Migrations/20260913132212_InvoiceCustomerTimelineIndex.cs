using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceCustomerTimelineIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_invoices_organisation_customer_posted_at",
                schema: "billing",
                table: "invoices",
                columns: new[] { "organisation_id", "customer_id", "posted_at" },
                filter: "posted_at IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_invoices_organisation_customer_posted_at",
                schema: "billing",
                table: "invoices");
        }
    }
}
