using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Customers.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerExports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_exports",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    document_version = table.Column<int>(type: "integer", nullable: false),
                    classification = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    document = table.Column<byte[]>(type: "bytea", nullable: true),
                    byte_count = table.Column<int>(type: "integer", nullable: false),
                    content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    generated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    download_count = table.Column<int>(type: "integer", nullable: false),
                    last_downloaded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    purged_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    purge_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_exports", x => x.id);
                    table.CheckConstraint("ck_customer_exports_expires_after_generated", "expires_at > generated_at");
                    table.CheckConstraint("ck_customer_exports_purge_is_consistent", "(purged_at IS NULL) = (purge_reason IS NULL) AND (purged_at IS NULL OR document IS NULL)");
                    table.ForeignKey(
                        name: "fk_customer_exports_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "customers",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_exports_live_by_customer",
                schema: "customers",
                table: "customer_exports",
                columns: new[] { "customer_id", "generated_at" },
                descending: new[] { false, true },
                filter: "purged_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_customer_exports_pending_purge",
                schema: "customers",
                table: "customer_exports",
                column: "expires_at",
                filter: "purged_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_exports",
                schema: "customers");
        }
    }
}
