using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CalculationSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "calculation_snapshots",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    price_list_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_configuration_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gst_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    request = table.Column<string>(type: "jsonb", nullable: false),
                    result = table.Column<string>(type: "jsonb", nullable: false),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    calculated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calculation_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_calculation_snapshots_gst_registrations",
                        column: x => x.gst_registration_id,
                        principalSchema: "billing",
                        principalTable: "gst_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_calculation_snapshots_price_list_versions",
                        column: x => x.price_list_version_id,
                        principalSchema: "billing",
                        principalTable: "price_list_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_calculation_snapshots_tax_configuration_versions",
                        column: x => x.tax_configuration_version_id,
                        principalSchema: "billing",
                        principalTable: "tax_configuration_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calculation_snapshots_gst_registration_id",
                schema: "billing",
                table: "calculation_snapshots",
                column: "gst_registration_id");

            migrationBuilder.CreateIndex(
                name: "ix_calculation_snapshots_organisation_branch_calculated_at",
                schema: "billing",
                table: "calculation_snapshots",
                columns: new[] { "organisation_id", "branch_id", "calculated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_calculation_snapshots_price_list_version_id",
                schema: "billing",
                table: "calculation_snapshots",
                column: "price_list_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_calculation_snapshots_tax_configuration_version_id",
                schema: "billing",
                table: "calculation_snapshots",
                column: "tax_configuration_version_id");

            migrationBuilder.CreateIndex(
                name: "ux_calculation_snapshots_organisation_reference",
                schema: "billing",
                table: "calculation_snapshots",
                columns: new[] { "organisation_id", "reference" },
                unique: true);
            // What the model cannot express: a calculation snapshot is append-only (INV-INV-03, plan D10).
            // A figure that was quoted, confirmed or invoiced must reproduce exactly after every later
            // publication, so a row is written once and never updated or deleted from the application
            // role; the same shape as platform.audit_events.
            migrationBuilder.Sql("""
                CREATE FUNCTION billing.calculation_snapshots_are_append_only()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION
                        'billing.calculation_snapshots is append-only; % is not permitted', TG_OP
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER calculation_snapshots_are_append_only
                BEFORE UPDATE OR DELETE ON billing.calculation_snapshots
                FOR EACH ROW EXECUTE FUNCTION billing.calculation_snapshots_are_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The trigger goes with its table; the function does not.
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS billing.calculation_snapshots_are_append_only() CASCADE;");

            migrationBuilder.DropTable(
                name: "calculation_snapshots",
                schema: "billing");
        }
    }
}
