using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PriceLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "price_lists",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_lists", x => x.id);
                    table.CheckConstraint("ck_price_lists_code_is_well_formed", "code ~ '^[A-Z][A-Z0-9_]+$'");
                });

            migrationBuilder.CreateTable(
                name: "price_list_versions",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    tax_inclusive = table.Column<bool>(type: "boolean", nullable: false),
                    round_off = table.Column<int>(type: "integer", nullable: false),
                    override_threshold_percent = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    cloned_from_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    published_by = table.Column<Guid>(type: "uuid", nullable: true),
                    publish_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    retired_by = table.Column<Guid>(type: "uuid", nullable: true),
                    retired_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_list_versions", x => x.id);
                    table.CheckConstraint("ck_price_list_versions_lifecycle_is_consistent", "(status = 0 AND published_at IS NULL AND retired_at IS NULL)\nOR (status = 1 AND published_at IS NOT NULL AND retired_at IS NULL)\nOR (status = 2 AND published_at IS NOT NULL AND retired_at IS NOT NULL)");
                    table.CheckConstraint("ck_price_list_versions_number_is_positive", "version_number >= 1");
                    table.CheckConstraint("ck_price_list_versions_threshold_is_a_percentage", "override_threshold_percent >= 0 AND override_threshold_percent <= 100");
                    table.ForeignKey(
                        name: "fk_price_list_versions_price_lists_price_list_id",
                        column: x => x.price_list_id,
                        principalSchema: "billing",
                        principalTable: "price_lists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "discount_rules",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    discount_rule_key = table.Column<Guid>(type: "uuid", nullable: false),
                    price_list_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    maximum_without_approval = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    maximum = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discount_rules", x => x.id);
                    table.CheckConstraint("ck_discount_rules_bounds_are_ordered", "maximum_without_approval >= 0 AND maximum_without_approval <= maximum");
                    table.CheckConstraint("ck_discount_rules_code_is_well_formed", "code ~ '^[A-Z][A-Z0-9_]+$'");
                    table.ForeignKey(
                        name: "fk_discount_rules_price_list_versions_price_list_version_id",
                        column: x => x.price_list_version_id,
                        principalSchema: "billing",
                        principalTable: "price_list_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "price_list_items",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_list_item_key = table.Column<Guid>(type: "uuid", nullable: false),
                    price_list_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    base_rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tax_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_list_items", x => x.id);
                    table.CheckConstraint("ck_price_list_items_code_is_well_formed", "code ~ '^[A-Z][A-Z0-9_]+$'");
                    table.CheckConstraint("ck_price_list_items_rate_is_not_negative", "base_rate >= 0");
                    table.ForeignKey(
                        name: "fk_price_list_items_price_list_versions_price_list_version_id",
                        column: x => x.price_list_version_id,
                        principalSchema: "billing",
                        principalTable: "price_list_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "price_list_version_branches",
                schema: "billing",
                columns: table => new
                {
                    price_list_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_list_version_branches", x => new { x.price_list_version_id, x.branch_id });
                    table.ForeignKey(
                        name: "fk_price_list_version_branches_price_list_versions",
                        column: x => x.price_list_version_id,
                        principalSchema: "billing",
                        principalTable: "price_list_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_discount_rules_version_code",
                schema: "billing",
                table: "discount_rules",
                columns: new[] { "price_list_version_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_discount_rules_version_key",
                schema: "billing",
                table: "discount_rules",
                columns: new[] { "price_list_version_id", "discount_rule_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_price_list_items_version_code",
                schema: "billing",
                table: "price_list_items",
                columns: new[] { "price_list_version_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_price_list_items_version_key",
                schema: "billing",
                table: "price_list_items",
                columns: new[] { "price_list_version_id", "price_list_item_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_price_list_versions_organisation_status",
                schema: "billing",
                table: "price_list_versions",
                columns: new[] { "organisation_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_price_list_versions_list_number",
                schema: "billing",
                table: "price_list_versions",
                columns: new[] { "price_list_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_price_lists_organisation_code",
                schema: "billing",
                table: "price_lists",
                columns: new[] { "organisation_id", "code" },
                unique: true);
            // What the model cannot express, each with the reason it is here.

            // 1. Exactly one published version per price list, judged at COMMIT for the reason the tax
            // configuration's constraint is: retiring one version and publishing another is one save
            // whose two updates run in primary-key order.
            migrationBuilder.Sql("""
                ALTER TABLE billing.price_list_versions
                ADD CONSTRAINT ux_price_list_versions_one_published
                EXCLUDE USING btree (price_list_id WITH =) WHERE (status = 1)
                DEFERRABLE INITIALLY DEFERRED;
                """);

            // 2. A published price-list version is immutable (INV-INV-03): every confirmed order's price
            // snapshot and every invoice is pinned to the version it was calculated on. The version
            // row moves draft → published → retired one step at a time and never back; after
            // publication only the retirement columns and the last-touched pair may change.
            migrationBuilder.Sql("""
                CREATE FUNCTION billing.price_list_versions_are_immutable()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    permitted text[] :=
                        ARRAY['status', 'retired_at', 'retired_by', 'retired_reason',
                              'updated_at', 'updated_by'];
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        IF NEW.status = 0 THEN
                            RETURN NEW;
                        END IF;

                        RAISE EXCEPTION
                            'A price-list version is created as a draft. Status % is reached by '
                            'publishing or retiring it, not by inserting it.', NEW.status
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        IF OLD.status = 0 THEN
                            RETURN OLD;
                        END IF;

                        RAISE EXCEPTION
                            'A published or retired price-list version cannot be deleted. Orders and '
                            'invoices are pinned to it.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF NEW.status IS DISTINCT FROM OLD.status
                       AND NOT (OLD.status = 0 AND NEW.status = 1)
                       AND NOT (OLD.status = 1 AND NEW.status = 2) THEN
                        RAISE EXCEPTION
                            'A price-list version moves from draft to published to retired, one step '
                            'at a time and never back. % to % is not a transition.', OLD.status, NEW.status
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF OLD.status = 0 THEN
                        RETURN NEW;
                    END IF;

                    IF to_jsonb(NEW) - permitted IS DISTINCT FROM to_jsonb(OLD) - permitted THEN
                        RAISE EXCEPTION
                            'A published price-list version is immutable. Clone it to a new draft, '
                            'make the change there and publish that.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER price_list_versions_are_immutable
                BEFORE INSERT OR UPDATE OR DELETE ON billing.price_list_versions
                FOR EACH ROW EXECUTE FUNCTION billing.price_list_versions_are_immutable();
                """);

            // 3. The items, discount rules and branch rows of a published version: nothing may be
            // inserted, changed or deleted, and a row may not be moved between versions, so an UPDATE
            // is judged against the parent it leaves as well as the one it joins. One function serves
            // the three tables because each names its version directly.
            migrationBuilder.Sql("""
                CREATE FUNCTION billing.published_price_list_rows_are_immutable()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    old_status integer;
                    new_status integer;
                BEGIN
                    -- The version's own trigger (4 below) maintains the branch rows' published flag when the
                    -- version's status moves. That write comes from the database, not the application role,
                    -- and is the one change a published version's rows are allowed.
                    IF pg_trigger_depth() > 1 THEN
                        RETURN COALESCE(NEW, OLD);
                    END IF;

                    IF TG_OP <> 'INSERT' THEN
                        SELECT status INTO old_status
                        FROM billing.price_list_versions
                        WHERE id = OLD.price_list_version_id;
                    END IF;

                    IF TG_OP <> 'DELETE' THEN
                        SELECT status INTO new_status
                        FROM billing.price_list_versions
                        WHERE id = NEW.price_list_version_id;
                    END IF;

                    IF COALESCE(old_status, 0) = 0 AND COALESCE(new_status, 0) = 0 THEN
                        RETURN COALESCE(NEW, OLD);
                    END IF;

                    RAISE EXCEPTION
                        'billing.% belongs to a published or retired price-list version and cannot '
                        'change. Clone the version to a new draft and publish that.',
                        TG_TABLE_NAME
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER price_list_items_published_are_immutable
                BEFORE INSERT OR UPDATE OR DELETE ON billing.price_list_items
                FOR EACH ROW EXECUTE FUNCTION billing.published_price_list_rows_are_immutable();
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER discount_rules_published_are_immutable
                BEFORE INSERT OR UPDATE OR DELETE ON billing.discount_rules
                FOR EACH ROW EXECUTE FUNCTION billing.published_price_list_rows_are_immutable();
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER price_list_version_branches_published_are_immutable
                BEFORE INSERT OR UPDATE OR DELETE ON billing.price_list_version_branches
                FOR EACH ROW EXECUTE FUNCTION billing.published_price_list_rows_are_immutable();
                """);

            // 4. A branch is priced by at most one published version across every list, judged at COMMIT.
            // The publication check reads the other lists' published versions before it writes, and two
            // administrators publishing two lists' drafts for one branch in the same moment each read the
            // other's as unpublished; the calculation engine would then price the branch from whichever
            // version it loaded first. The rule is over a column the model does not map: `published` is
            // the parent's status, denormalised onto the branch rows by the parent's own trigger, because
            // an exclusion constraint cannot reach across the join.
            migrationBuilder.Sql("""
                ALTER TABLE billing.price_list_version_branches
                ADD COLUMN published boolean NOT NULL DEFAULT false;
                """);

            migrationBuilder.Sql("""
                CREATE FUNCTION billing.price_list_version_branches_follow_status()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    UPDATE billing.price_list_version_branches
                       SET published = (NEW.status = 1)
                     WHERE price_list_version_id = NEW.id;

                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER price_list_version_branches_follow_status
                AFTER UPDATE OF status ON billing.price_list_versions
                FOR EACH ROW EXECUTE FUNCTION billing.price_list_version_branches_follow_status();
                """);

            migrationBuilder.Sql("""
                ALTER TABLE billing.price_list_version_branches
                ADD CONSTRAINT ex_price_list_version_branches_one_published
                EXCLUDE USING btree (branch_id WITH =) WHERE (published)
                DEFERRABLE INITIALLY DEFERRED;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The triggers go with their tables; the functions and the constraint do not.
            migrationBuilder.Sql("ALTER TABLE billing.price_list_version_branches DROP CONSTRAINT IF EXISTS ex_price_list_version_branches_one_published;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS billing.price_list_version_branches_follow_status() CASCADE;");
            migrationBuilder.Sql("ALTER TABLE billing.price_list_versions DROP CONSTRAINT IF EXISTS ux_price_list_versions_one_published;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS billing.price_list_versions_are_immutable() CASCADE;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS billing.published_price_list_rows_are_immutable() CASCADE;");

            migrationBuilder.DropTable(
                name: "discount_rules",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "price_list_items",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "price_list_version_branches",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "price_list_versions",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "price_lists",
                schema: "billing");
        }
    }
}
