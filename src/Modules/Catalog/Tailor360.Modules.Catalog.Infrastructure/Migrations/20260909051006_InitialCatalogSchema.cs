using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalogSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "catalog_versions",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    cloned_from_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    published_by = table.Column<Guid>(type: "uuid", nullable: true),
                    publish_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    retired_by = table.Column<Guid>(type: "uuid", nullable: true),
                    retired_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalog_versions", x => x.id);
                    table.CheckConstraint("ck_catalog_versions_lifecycle_is_consistent", "(status = 0 AND published_at IS NULL AND retired_at IS NULL)\nOR (status = 1 AND published_at IS NOT NULL AND retired_at IS NULL)\nOR (status = 2 AND published_at IS NOT NULL AND retired_at IS NOT NULL)");
                    table.CheckConstraint("ck_catalog_versions_number_is_positive", "version_number >= 1");
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "catalog",
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
                name: "outbox_messages",
                schema: "catalog",
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
                name: "categories",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_key = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    name_tamil = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    active_from = table.Column<DateOnly>(type: "date", nullable: true),
                    active_to = table.Column<DateOnly>(type: "date", nullable: true),
                    feature_flag_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                    table.CheckConstraint("ck_categories_active_dates_are_ordered", "active_from IS NULL OR active_to IS NULL OR active_to >= active_from");
                    table.CheckConstraint("ck_categories_code_is_well_formed", "code ~ '^[A-Z][A-Z0-9_]*$'");
                    table.CheckConstraint("ck_categories_display_order_is_not_negative", "display_order >= 0");
                    table.CheckConstraint("ck_categories_parent_is_not_self", "parent_id IS NULL OR parent_id <> id");
                    table.ForeignKey(
                        name: "fk_categories_catalog_versions_catalog_version_id",
                        column: x => x.catalog_version_id,
                        principalSchema: "catalog",
                        principalTable: "catalog_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_categories_categories_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "catalog",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "category_branches",
                schema: "catalog",
                columns: table => new
                {
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_category_branches", x => new { x.category_id, x.branch_id });
                    table.ForeignKey(
                        name: "fk_category_branches_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "catalog",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_types",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_type_key = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    name_tamil = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    expected_duration_days = table.Column<int>(type: "integer", nullable: false),
                    intake_warning = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    measurement_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    workflow_definition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    price_list_item_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    qc_checklist_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    allow_incomplete = table.Column<bool>(type: "boolean", nullable: false),
                    not_orderable = table.Column<bool>(type: "boolean", nullable: false),
                    active_from = table.Column<DateOnly>(type: "date", nullable: true),
                    active_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_types", x => x.id);
                    table.CheckConstraint("ck_service_types_active_dates_are_ordered", "active_from IS NULL OR active_to IS NULL OR active_to >= active_from");
                    table.CheckConstraint("ck_service_types_code_is_well_formed", "code ~ '^[A-Z][A-Z0-9_]*$'");
                    table.CheckConstraint("ck_service_types_display_order_is_not_negative", "display_order >= 0");
                    table.CheckConstraint("ck_service_types_duration_is_in_range", "expected_duration_days BETWEEN 1 AND 250");
                    table.ForeignKey(
                        name: "fk_service_types_catalog_versions_catalog_version_id",
                        column: x => x.catalog_version_id,
                        principalSchema: "catalog",
                        principalTable: "catalog_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_service_types_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "catalog",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_type_branches",
                schema: "catalog",
                columns: table => new
                {
                    service_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_type_branches", x => new { x.service_type_id, x.branch_id });
                    table.ForeignKey(
                        name: "fk_service_type_branches_service_types_service_type_id",
                        column: x => x.service_type_id,
                        principalSchema: "catalog",
                        principalTable: "service_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_type_design_groups",
                schema: "catalog",
                columns: table => new
                {
                    service_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    design_option_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_type_design_groups", x => new { x.service_type_id, x.design_option_group_id });
                    table.ForeignKey(
                        name: "fk_service_type_design_groups_service_types_service_type_id",
                        column: x => x.service_type_id,
                        principalSchema: "catalog",
                        principalTable: "service_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_versions_organisation_status",
                schema: "catalog",
                table: "catalog_versions",
                columns: new[] { "organisation_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_catalog_versions_one_published",
                schema: "catalog",
                table: "catalog_versions",
                column: "organisation_id",
                unique: true,
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ux_catalog_versions_organisation_number",
                schema: "catalog",
                table: "catalog_versions",
                columns: new[] { "organisation_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_categories_parent",
                schema: "catalog",
                table: "categories",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ux_categories_version_code",
                schema: "catalog",
                table: "categories",
                columns: new[] { "catalog_version_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_categories_version_key",
                schema: "catalog",
                table: "categories",
                columns: new[] { "catalog_version_id", "category_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "catalog",
                table: "outbox_messages",
                columns: new[] { "available_at", "aggregate_id" },
                filter: "processed_at IS NULL AND dead_lettered_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at",
                schema: "catalog",
                table: "outbox_messages",
                column: "processed_at");

            migrationBuilder.CreateIndex(
                name: "ux_service_types_category_code",
                schema: "catalog",
                table: "service_types",
                columns: new[] { "category_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_service_types_version_key",
                schema: "catalog",
                table: "service_types",
                columns: new[] { "catalog_version_id", "service_type_key" },
                unique: true);

            // Immutability, in the database rather than only in the application.
            //
            // docs/prd/category-hierarchy.md section 7 says a published version is immutable and says
            // it about the data, not about a code path: "enforced by a database trigger, not only by
            // application code". The aggregate refuses an edit once a version leaves Draft, and this is
            // the half that holds when somebody reaches the tables with psql — which is exactly when it
            // matters, because a published version is what confirmed orders, job cards and invoices are
            // pinned to.
            //
            // The comparison is over to_jsonb(row) with the permitted columns removed. Naming what may
            // change rather than enumerating what may not means a column added by a later migration is
            // frozen by default: the failure mode of forgetting to update this is a refused write that
            // somebody investigates, not a silently mutable published version.
            migrationBuilder.Sql("""
                CREATE FUNCTION catalog.published_entries_are_immutable()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    version_status integer;
                    permitted text[] := ARRAY['name', 'name_tamil', 'description', 'display_order'];
                BEGIN
                    SELECT status INTO version_status
                    FROM catalog.catalog_versions
                    WHERE id = COALESCE(NEW.catalog_version_id, OLD.catalog_version_id);

                    -- A draft is freely editable, and a version that has already gone is not this
                    -- trigger's business: the cascade from catalog_versions is how a discarded draft
                    -- takes its tree with it.
                    IF version_status IS NULL OR version_status = 0 THEN
                        RETURN COALESCE(NEW, OLD);
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION
                            'catalog.% belongs to a published or retired catalogue version and cannot '
                            'be deleted. Clone the version to a new draft and publish that.',
                            TG_TABLE_NAME
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF to_jsonb(NEW) - permitted IS DISTINCT FROM to_jsonb(OLD) - permitted THEN
                        RAISE EXCEPTION
                            'catalog.% belongs to a published or retired catalogue version. Only the '
                            'label, Tamil label, description and display order may be corrected in '
                            'place; everything else is what confirmed orders are pinned to.',
                            TG_TABLE_NAME
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER categories_published_are_immutable
                BEFORE UPDATE OR DELETE ON catalog.categories
                FOR EACH ROW EXECUTE FUNCTION catalog.published_entries_are_immutable();
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER service_types_published_are_immutable
                BEFORE UPDATE OR DELETE ON catalog.service_types
                FOR EACH ROW EXECUTE FUNCTION catalog.published_entries_are_immutable();
                """);

            // The version row itself. Publication is a transition out of Draft and is allowed; after
            // that the only thing that may still move is retirement, plus the two columns that record
            // who last touched the version — which a presentation correction to one of its categories
            // does update.
            migrationBuilder.Sql("""
                CREATE FUNCTION catalog.catalog_versions_are_immutable()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    permitted text[] :=
                        ARRAY['status', 'retired_at', 'retired_by', 'retired_reason',
                              'updated_at', 'updated_by'];
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        IF OLD.status = 0 THEN
                            RETURN OLD;
                        END IF;

                        RAISE EXCEPTION
                            'A published or retired catalogue version cannot be deleted. Orders, job '
                            'cards and invoices are pinned to it and render from it.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF OLD.status = 0 THEN
                        RETURN NEW;
                    END IF;

                    IF to_jsonb(NEW) - permitted IS DISTINCT FROM to_jsonb(OLD) - permitted THEN
                        RAISE EXCEPTION
                            'A published catalogue version is immutable. Clone it to a new draft, make '
                            'the change there and publish that.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF NEW.status IS DISTINCT FROM OLD.status AND NOT (OLD.status = 1 AND NEW.status = 2) THEN
                        RAISE EXCEPTION
                            'A catalogue version moves from draft to published to retired and never '
                            'back. % to % is not a transition.', OLD.status, NEW.status
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER catalog_versions_are_immutable
                BEFORE UPDATE OR DELETE ON catalog.catalog_versions
                FOR EACH ROW EXECUTE FUNCTION catalog.catalog_versions_are_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The triggers before the tables they guard, or dropping the tables would fail against the
            // functions still bound to them; the functions after the triggers that use them.
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS catalog_versions_are_immutable ON catalog.catalog_versions;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS service_types_published_are_immutable ON catalog.service_types;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS categories_published_are_immutable ON catalog.categories;");

            migrationBuilder.DropTable(
                name: "category_branches",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "service_type_branches",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "service_type_design_groups",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "service_types",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "categories",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "catalog_versions",
                schema: "catalog");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS catalog.catalog_versions_are_immutable();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS catalog.published_entries_are_immutable();");
        }
    }
}
