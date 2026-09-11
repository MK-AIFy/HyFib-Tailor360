using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tailor360.Modules.Customers.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MeasurementCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "measurement_drafts",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reused_from_version_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_measurement_drafts", x => x.id);
                    table.CheckConstraint("ck_measurement_drafts_expires_after_it_started", "expires_at > started_at");
                });

            migrationBuilder.CreateTable(
                name: "measurement_versions",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    taken_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    taken_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reused_from_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    corrects_version_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_measurement_versions", x => x.id);
                    table.CheckConstraint("ck_measurement_versions_number_is_positive", "version_number >= 1");
                });

            migrationBuilder.CreateTable(
                name: "measurement_draft_values",
                schema: "customers",
                columns: table => new
                {
                    measurement_draft_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    field_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    millimetres = table.Column<decimal>(type: "numeric(12,3)", precision: 18, scale: 4, nullable: true),
                    choice = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    entered_unit = table.Column<int>(type: "integer", nullable: false),
                    acknowledged = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_measurement_draft_values", x => new { x.measurement_draft_id, x.id });
                    table.CheckConstraint("ck__is_measured_or_chosen", "(millimetres IS NULL) <> (choice IS NULL)");
                    table.ForeignKey(
                        name: "fk_measurement_draft_values_measurement_drafts_measurement_dra",
                        column: x => x.measurement_draft_id,
                        principalSchema: "customers",
                        principalTable: "measurement_drafts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "measurement_version_values",
                schema: "customers",
                columns: table => new
                {
                    measurement_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    field_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    millimetres = table.Column<decimal>(type: "numeric(12,3)", precision: 18, scale: 4, nullable: true),
                    choice = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    entered_unit = table.Column<int>(type: "integer", nullable: false),
                    acknowledged = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_measurement_version_values", x => new { x.measurement_version_id, x.id });
                    table.CheckConstraint("ck__is_measured_or_chosen", "(millimetres IS NULL) <> (choice IS NULL)");
                    table.ForeignKey(
                        name: "fk_measurement_version_values_measurement_versions_measurement",
                        column: x => x.measurement_version_id,
                        principalSchema: "customers",
                        principalTable: "measurement_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_measurement_drafts_organisation_expiry",
                schema: "customers",
                table: "measurement_drafts",
                columns: new[] { "organisation_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ux_measurement_drafts_one_open",
                schema: "customers",
                table: "measurement_drafts",
                columns: new[] { "branch_id", "customer_id", "template_id" },
                unique: true,
                filter: "consumed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_measurement_versions_customer_template_taken",
                schema: "customers",
                table: "measurement_versions",
                columns: new[] { "customer_id", "template_id", "taken_at" });

            migrationBuilder.CreateIndex(
                name: "ux_measurement_versions_customer_template_number",
                schema: "customers",
                table: "measurement_versions",
                columns: new[] { "customer_id", "template_id", "version_number" },
                unique: true);

            // INV-MSR-01: a measurement version is never edited. A change is a new version with a reason, and the
            // old one stays readable — because a garment job snapshots the version it was confirmed against
            // (INV-JOB-01), so editing one would change what a job already in production was cut to.
            //
            // The domain type publishes no mutator. This is the half that holds when somebody reaches the table
            // with psql, and it follows the pattern customers.consent_records and platform.audit_events use.
            // Both halves, because either alone has been enough to fail before.
            migrationBuilder.Sql("""
                CREATE FUNCTION customers.measurement_versions_append_only()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION
                        'customers.measurement_versions is append-only; % is not permitted. Correcting a '
                        'measurement inserts a new version with a reason; it does not change the one it '
                        'replaces.', TG_OP
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER measurement_versions_no_update
                BEFORE UPDATE OR DELETE ON customers.measurement_versions
                FOR EACH ROW EXECUTE FUNCTION customers.measurement_versions_append_only();
                """);

            // The values are the measurement. Freezing the row that owns them and leaving these writable would
            // let somebody change what was measured while the record of who took it stayed put.
            migrationBuilder.Sql("""
                CREATE TRIGGER measurement_version_values_no_update
                BEFORE UPDATE OR DELETE ON customers.measurement_version_values
                FOR EACH ROW EXECUTE FUNCTION customers.measurement_versions_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropped before the tables, so a re-run of Down on a partly applied migration does not leave a
            // trigger pointing at a function that is already gone.
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS measurement_version_values_no_update "
                + "ON customers.measurement_version_values;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS measurement_versions_no_update ON customers.measurement_versions;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS customers.measurement_versions_append_only();");

            migrationBuilder.DropTable(
                name: "measurement_draft_values",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "measurement_version_values",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "measurement_drafts",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "measurement_versions",
                schema: "customers");
        }
    }
}
