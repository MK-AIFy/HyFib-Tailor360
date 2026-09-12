using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DesignCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "design_option_groups",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    design_option_group_key = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    name_tamil = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    selection_mode = table.Column<int>(type: "integer", nullable: false),
                    required = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    active_from = table.Column<DateOnly>(type: "date", nullable: true),
                    active_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_design_option_groups", x => x.id);
                    table.UniqueConstraint("ak_design_option_groups_id_catalog_version_id", x => new { x.id, x.catalog_version_id });
                    table.CheckConstraint("ck_design_option_groups_active_dates_are_ordered", "active_from IS NULL OR active_to IS NULL OR active_to >= active_from");
                    table.CheckConstraint("ck_design_option_groups_code_is_well_formed", "code ~ '^[a-z][a-z0-9_]*$'");
                    table.CheckConstraint("ck_design_option_groups_display_order_is_not_negative", "display_order >= 0");
                    table.ForeignKey(
                        name: "fk_design_option_groups_catalog_versions_catalog_version_id",
                        column: x => x.catalog_version_id,
                        principalSchema: "catalog",
                        principalTable: "catalog_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_design_option_groups_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "catalog",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "design_rules",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    design_rule_key = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_number = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    antecedent_group_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    antecedent_form = table.Column<int>(type: "integer", nullable: false),
                    antecedent_option_codes = table.Column<string[]>(type: "text[]", nullable: false),
                    consequent_group_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    consequent_form = table.Column<int>(type: "integer", nullable: true),
                    consequent_option_codes = table.Column<string[]>(type: "text[]", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    why = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_design_rules", x => x.id);
                    table.CheckConstraint("ck_design_rules_number_is_positive", "rule_number >= 1");
                    table.ForeignKey(
                        name: "fk_design_rules_catalog_versions_catalog_version_id",
                        column: x => x.catalog_version_id,
                        principalSchema: "catalog",
                        principalTable: "catalog_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_design_rules_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "catalog",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "design_option_group_branches",
                schema: "catalog",
                columns: table => new
                {
                    design_option_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_design_option_group_branches", x => new { x.design_option_group_id, x.branch_id });
                    table.ForeignKey(
                        name: "fk_design_option_group_branches_design_option_groups_design_op",
                        column: x => x.design_option_group_id,
                        principalSchema: "catalog",
                        principalTable: "design_option_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "design_options",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    design_option_key = table.Column<Guid>(type: "uuid", nullable: false),
                    design_option_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    name_tamil = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    help_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    illustration_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    illustration_alt = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    price_list_item_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    time_impact_days = table.Column<int>(type: "integer", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_design_options", x => x.id);
                    table.CheckConstraint("ck_design_options_code_is_well_formed", "code ~ '^[A-Z][A-Z0-9_]*$'");
                    table.CheckConstraint("ck_design_options_display_order_is_not_negative", "display_order >= 0");
                    table.CheckConstraint("ck_design_options_time_impact_is_in_range", "time_impact_days BETWEEN -250 AND 250");
                    table.ForeignKey(
                        name: "fk_design_options_design_option_groups_design_option_group_id_",
                        columns: x => new { x.design_option_group_id, x.catalog_version_id },
                        principalSchema: "catalog",
                        principalTable: "design_option_groups",
                        principalColumns: new[] { "id", "catalog_version_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_design_option_groups_category_code",
                schema: "catalog",
                table: "design_option_groups",
                columns: new[] { "category_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_design_option_groups_version_key",
                schema: "catalog",
                table: "design_option_groups",
                columns: new[] { "catalog_version_id", "design_option_group_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_design_options_design_option_group_id_catalog_version_id",
                schema: "catalog",
                table: "design_options",
                columns: new[] { "design_option_group_id", "catalog_version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_design_options_group_code",
                schema: "catalog",
                table: "design_options",
                columns: new[] { "design_option_group_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_design_options_version_key",
                schema: "catalog",
                table: "design_options",
                columns: new[] { "catalog_version_id", "design_option_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_design_rules_category_id",
                schema: "catalog",
                table: "design_rules",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ux_design_rules_version_key",
                schema: "catalog",
                table: "design_rules",
                columns: new[] { "catalog_version_id", "design_rule_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_design_rules_version_number",
                schema: "catalog",
                table: "design_rules",
                columns: new[] { "catalog_version_id", "rule_number" },
                unique: true);
            // docs/prd/design-options.md section 3: a group or an option is never deleted, and a
            // published version's rows never change but for their words. The same trigger the
            // categories and service types carry (InitialCatalogSchema) is attached to the groups and
            // the rules, whose rows name their version directly; the options get their own copy with
            // the wider list of words they may correct, and the branch rows — which name no version —
            // find theirs through the group.
            migrationBuilder.Sql("""
                CREATE TRIGGER design_option_groups_published_are_immutable
                BEFORE INSERT OR UPDATE OR DELETE ON catalog.design_option_groups
                FOR EACH ROW EXECUTE FUNCTION catalog.published_entries_are_immutable();
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER design_rules_published_are_immutable
                BEFORE INSERT OR UPDATE OR DELETE ON catalog.design_rules
                FOR EACH ROW EXECUTE FUNCTION catalog.published_entries_are_immutable();
                """);

            migrationBuilder.Sql("""
                CREATE FUNCTION catalog.published_design_options_are_immutable()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    version_status integer;
                    permitted text[] :=
                        ARRAY['name', 'name_tamil', 'help_text', 'illustration_alt', 'display_order'];
                BEGIN
                    SELECT status INTO version_status
                    FROM catalog.catalog_versions
                    WHERE id = COALESCE(NEW.catalog_version_id, OLD.catalog_version_id);

                    IF version_status IS NULL OR version_status = 0 THEN
                        RETURN COALESCE(NEW, OLD);
                    END IF;

                    IF TG_OP = 'INSERT' THEN
                        RAISE EXCEPTION
                            'catalog.design_options cannot be added to a published or retired catalogue '
                            'version. Clone the version to a new draft, add it there and publish that.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION
                            'catalog.design_options belongs to a published or retired catalogue version '
                            'and cannot be deleted. Retire the option in a new draft instead; confirmed '
                            'garments refer to it.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF to_jsonb(NEW) - permitted IS DISTINCT FROM to_jsonb(OLD) - permitted THEN
                        RAISE EXCEPTION
                            'catalog.design_options belongs to a published or retired catalogue version. '
                            'Only the label, Tamil label, help text, alternative text and display order '
                            'may be corrected in place; everything else is what confirmed garments are '
                            'pinned to.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER design_options_published_are_immutable
                BEFORE INSERT OR UPDATE OR DELETE ON catalog.design_options
                FOR EACH ROW EXECUTE FUNCTION catalog.published_design_options_are_immutable();
                """);

            migrationBuilder.Sql("""
                CREATE FUNCTION catalog.published_design_group_branches_are_immutable()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    version_status integer;
                BEGIN
                    SELECT v.status INTO version_status
                    FROM catalog.design_option_groups g
                    JOIN catalog.catalog_versions v ON v.id = g.catalog_version_id
                    WHERE g.id = COALESCE(NEW.design_option_group_id, OLD.design_option_group_id);

                    IF version_status IS NULL OR version_status = 0 THEN
                        RETURN COALESCE(NEW, OLD);
                    END IF;

                    RAISE EXCEPTION
                        'catalog.design_option_group_branches belongs to a published or retired '
                        'catalogue version and cannot change. Where a group is offered is part of what '
                        'was published; clone the version to a new draft and change it there.'
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER design_option_group_branches_published_are_immutable
                BEFORE INSERT OR UPDATE OR DELETE ON catalog.design_option_group_branches
                FOR EACH ROW EXECUTE FUNCTION catalog.published_design_group_branches_are_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS design_option_group_branches_published_are_immutable "
                + "ON catalog.design_option_group_branches;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS catalog.published_design_group_branches_are_immutable();");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS design_options_published_are_immutable ON catalog.design_options;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS catalog.published_design_options_are_immutable();");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS design_rules_published_are_immutable ON catalog.design_rules;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS design_option_groups_published_are_immutable "
                + "ON catalog.design_option_groups;");

            migrationBuilder.DropTable(
                name: "design_option_group_branches",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "design_options",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "design_rules",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "design_option_groups",
                schema: "catalog");
        }
    }
}
