using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Customers.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMeasurementTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "measurement_templates",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_measurement_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "measurement_template_versions",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    measurement_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    default_display_unit = table.Column<int>(type: "integer", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    published_by = table.Column<Guid>(type: "uuid", nullable: true),
                    publish_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    retired_by = table.Column<Guid>(type: "uuid", nullable: true),
                    retired_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_measurement_template_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_measurement_template_versions_measurement_templates_measure",
                        column: x => x.measurement_template_id,
                        principalSchema: "customers",
                        principalTable: "measurement_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "measurement_template_fields",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    label_tamil = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    group_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    canonical_unit = table.Column<int>(type: "integer", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    help_text = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    diagram_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    diagram_media_id = table.Column<Guid>(type: "uuid", nullable: true),
                    diagram_alt = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    rule = table.Column<string>(type: "jsonb", nullable: true),
                    options = table.Column<string>(type: "jsonb", nullable: false),
                    maximum_mm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    minimum_mm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    warn_above_mm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    warn_below_mm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    centimetre_decimals = table.Column<int>(type: "integer", nullable: false),
                    inch_fraction = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_measurement_template_fields", x => x.id);
                    table.ForeignKey(
                        name: "fk_measurement_template_fields_measurement_template_versions_t",
                        column: x => x.template_version_id,
                        principalSchema: "customers",
                        principalTable: "measurement_template_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_template_fields_version_key",
                schema: "customers",
                table: "measurement_template_fields",
                columns: new[] { "template_version_id", "field_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_template_versions_one_published",
                schema: "customers",
                table: "measurement_template_versions",
                column: "measurement_template_id",
                unique: true,
                filter: "status = 2");

            migrationBuilder.CreateIndex(
                name: "ux_template_versions_template_number",
                schema: "customers",
                table: "measurement_template_versions",
                columns: new[] { "measurement_template_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_measurement_templates_organisation_code",
                schema: "customers",
                table: "measurement_templates",
                columns: new[] { "organisation_id", "code" },
                unique: true);

            // Immutability, in the database rather than only in the application.
            //
            // A measurement captured under a published template version renders through that version's labels,
            // units, precision and rules forever. Letting a published version change would rewrite what a
            // customer's stored measurements mean, retrospectively and silently — so the aggregate refuses it and
            // this is the half that holds when somebody reaches the tables with psql.
            //
            // Unlike the catalogue's presentation correction, nothing is correctable in place here. Issue #27's
            // acceptance criteria say a published template cannot be edited, full stop; a label that reads wrongly
            // is fixed by cloning the version, correcting the clone and publishing it.
            migrationBuilder.Sql("""
                CREATE FUNCTION customers.template_fields_are_immutable()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    version_status integer;
                BEGIN
                    SELECT status INTO version_status
                    FROM customers.measurement_template_versions
                    WHERE id = COALESCE(NEW.template_version_id, OLD.template_version_id);

                    -- A draft is freely editable, and a version that has already gone is not this trigger's
                    -- business: the cascade from the version row is how a discarded draft takes its fields with it.
                    IF version_status IS NULL OR version_status = 0 THEN
                        RETURN COALESCE(NEW, OLD);
                    END IF;

                    RAISE EXCEPTION
                        'customers.measurement_template_fields belongs to a template version that is no longer a '
                        'draft, so it cannot be %. Clone the version to a new draft and change that.',
                        CASE TG_OP
                            WHEN 'INSERT' THEN 'added to'
                            WHEN 'UPDATE' THEN 'changed'
                            ELSE 'removed'
                        END
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER template_fields_are_immutable
                BEFORE INSERT OR UPDATE OR DELETE ON customers.measurement_template_fields
                FOR EACH ROW EXECUTE FUNCTION customers.template_fields_are_immutable();
                """);

            // The version row itself. Publication and retirement are transitions and are allowed; after
            // retirement nothing moves, and the lifecycle only ever runs one way.
            migrationBuilder.Sql("""
                CREATE FUNCTION customers.template_versions_are_immutable()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    permitted text[] :=
                        ARRAY['status', 'published_at', 'published_by', 'publish_reason',
                              'retired_at', 'retired_by', 'retired_reason',
                              'submitted_at', 'submitted_by', 'approved_at', 'approved_by',
                              'updated_at', 'updated_by'];
                BEGIN
                    -- A version is created as a draft and reaches every other state by being moved there. An
                    -- insert arriving already published would skip the review, the supersession of the version it
                    -- replaces, and the audit entry recording who decided.
                    IF TG_OP = 'INSERT' THEN
                        IF NEW.status = 0 THEN
                            RETURN NEW;
                        END IF;

                        RAISE EXCEPTION
                            'A measurement template version is created as a draft. Status % is reached by '
                            'submitting, publishing or retiring it, not by inserting it.', NEW.status
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        IF OLD.status = 0 THEN
                            RETURN OLD;
                        END IF;

                        RAISE EXCEPTION
                            'A submitted, published or retired template version cannot be deleted. Measurements '
                            'are pinned to it and render from it.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF OLD.status IN (2, 3)
                       AND to_jsonb(NEW) - permitted IS DISTINCT FROM to_jsonb(OLD) - permitted THEN
                        RAISE EXCEPTION
                            'A published or retired template version is immutable. Clone it to a new draft, make '
                            'the change there and publish that.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF NEW.status IS DISTINCT FROM OLD.status
                       AND NOT (
                           (OLD.status = 0 AND NEW.status = 1)
                           OR (OLD.status = 1 AND NEW.status = 0)
                           OR (OLD.status = 1 AND NEW.status = 2)
                           OR (OLD.status = 2 AND NEW.status = 3)) THEN
                        RAISE EXCEPTION
                            'A template version moves from draft to review to published to retired, and back to '
                            'draft only from review. % to % is not a transition.', OLD.status, NEW.status
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER template_versions_are_immutable
                BEFORE INSERT OR UPDATE OR DELETE ON customers.measurement_template_versions
                FOR EACH ROW EXECUTE FUNCTION customers.template_versions_are_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Triggers go with their tables, but the functions do not: dropping the table leaves the function
            // behind, and a re-applied migration would then fail on CREATE FUNCTION.
            migrationBuilder.DropTable(
                name: "measurement_template_fields",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "measurement_template_versions",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "measurement_templates",
                schema: "customers");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS customers.template_fields_are_immutable();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS customers.template_versions_are_immutable();");
        }
    }
}
