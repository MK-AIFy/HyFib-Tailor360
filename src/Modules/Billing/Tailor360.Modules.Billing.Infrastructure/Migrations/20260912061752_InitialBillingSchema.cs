using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialBillingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "billing");

            migrationBuilder.CreateTable(
                name: "gst_registrations",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gstin = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    state_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    trade_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gst_registrations", x => x.id);
                    table.CheckConstraint("ck_gst_registrations_dates_are_ordered", "effective_to IS NULL OR effective_to >= effective_from");
                    table.CheckConstraint("ck_gst_registrations_gstin_shape", "gstin ~ '^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][0-9A-Z]Z[0-9A-Z]$'");
                    table.CheckConstraint("ck_gst_registrations_state_code_shape", "state_code ~ '^[0-9]{2}$'");
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "billing",
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
                schema: "billing",
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
                name: "tax_configuration_versions",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
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
                    table.PrimaryKey("pk_tax_configuration_versions", x => x.id);
                    table.CheckConstraint("ck_tax_configuration_versions_lifecycle_is_consistent", "(status = 0 AND published_at IS NULL AND retired_at IS NULL)\nOR (status = 1 AND published_at IS NOT NULL AND retired_at IS NULL)\nOR (status = 2 AND published_at IS NOT NULL AND retired_at IS NOT NULL)");
                    table.CheckConstraint("ck_tax_configuration_versions_number_is_positive", "version_number >= 1");
                });

            migrationBuilder.CreateTable(
                name: "tax_codes",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_code_key = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_configuration_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    classification = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_codes", x => x.id);
                    table.CheckConstraint("ck_tax_codes_classification_is_digits", "classification ~ '^[0-9]{4,8}$'");
                    table.CheckConstraint("ck_tax_codes_code_is_well_formed", "code ~ '^[A-Z][A-Z0-9_]+$'");
                    table.ForeignKey(
                        name: "fk_tax_codes_tax_configuration_versions",
                        column: x => x.tax_configuration_version_id,
                        principalSchema: "billing",
                        principalTable: "tax_configuration_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_components",
                schema: "billing",
                columns: table => new
                {
                    tax_code_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    rate_percent = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_components", x => new { x.tax_code_id, x.kind });
                    table.CheckConstraint("ck_tax_components_rate_is_a_percentage", "rate_percent >= 0 AND rate_percent <= 100");
                    table.ForeignKey(
                        name: "fk_tax_components_tax_codes_tax_code_id",
                        column: x => x.tax_code_id,
                        principalSchema: "billing",
                        principalTable: "tax_codes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gst_registrations_branch_effective_from",
                schema: "billing",
                table: "gst_registrations",
                columns: new[] { "organisation_id", "branch_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "billing",
                table: "outbox_messages",
                columns: new[] { "available_at", "aggregate_id" },
                filter: "processed_at IS NULL AND dead_lettered_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at",
                schema: "billing",
                table: "outbox_messages",
                column: "processed_at");

            migrationBuilder.CreateIndex(
                name: "ux_tax_codes_version_code",
                schema: "billing",
                table: "tax_codes",
                columns: new[] { "tax_configuration_version_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_tax_codes_version_key",
                schema: "billing",
                table: "tax_codes",
                columns: new[] { "tax_configuration_version_id", "tax_code_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_configuration_versions_organisation_status",
                schema: "billing",
                table: "tax_configuration_versions",
                columns: new[] { "organisation_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_tax_configuration_versions_organisation_number",
                schema: "billing",
                table: "tax_configuration_versions",
                columns: new[] { "organisation_id", "version_number" },
                unique: true);
            // What the model cannot express, each with the reason it is here.

            // 1. Exactly one published version per organisation, checked at COMMIT rather than per
            // statement. Publishing retires the outgoing version and publishes the draft in one save,
            // and the two UPDATEs run in primary-key order — so a draft older than the version it
            // supersedes would trip a per-statement index before the retirement had landed. A
            // deferred exclusion constraint judges the transaction as a whole; two administrators
            // publishing at once are still settled by it, at commit.
            migrationBuilder.Sql("""
                ALTER TABLE billing.tax_configuration_versions
                ADD CONSTRAINT ux_tax_configuration_versions_one_published
                EXCLUDE USING btree (organisation_id WITH =) WHERE (status = 1)
                DEFERRABLE INITIALLY DEFERRED;
                """);

            // 2. At most one registration of a branch is in force on any day. Two independent date
            // columns cannot say that as a unique index; an exclusion constraint over the closed date
            // range can, and it also settles the race the handler's read cannot see. btree_gist is
            // what lets `branch_id WITH =` sit in a GiST index beside the range.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            migrationBuilder.Sql("""
                ALTER TABLE billing.gst_registrations
                ADD CONSTRAINT ex_gst_registrations_one_in_force
                EXCLUDE USING gist (branch_id WITH =, daterange(effective_from, effective_to, '[]') WITH &&);
                """);

            // 3. A published tax configuration version is immutable (INV-INV-03: every calculation
            // is pinned to the version it was made on). The version row moves from draft to
            // published to retired, one step at a time and never back, and only the retirement
            // columns and the last-touched pair may change after publication.
            migrationBuilder.Sql("""
                CREATE FUNCTION billing.tax_configuration_versions_are_immutable()
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
                            'A tax configuration version is created as a draft. Status % is reached by '
                            'publishing or retiring it, not by inserting it.', NEW.status
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        IF OLD.status = 0 THEN
                            RETURN OLD;
                        END IF;

                        RAISE EXCEPTION
                            'A published or retired tax configuration version cannot be deleted. '
                            'Invoices and calculation snapshots are pinned to it.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF NEW.status IS DISTINCT FROM OLD.status
                       AND NOT (OLD.status = 0 AND NEW.status = 1)
                       AND NOT (OLD.status = 1 AND NEW.status = 2) THEN
                        RAISE EXCEPTION
                            'A tax configuration version moves from draft to published to retired, one '
                            'step at a time and never back. % to % is not a transition.', OLD.status, NEW.status
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    IF OLD.status = 0 THEN
                        RETURN NEW;
                    END IF;

                    IF to_jsonb(NEW) - permitted IS DISTINCT FROM to_jsonb(OLD) - permitted THEN
                        RAISE EXCEPTION
                            'A published tax configuration version is immutable. Clone it to a new '
                            'draft, make the change there and publish that.'
                            USING ERRCODE = 'restrict_violation';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER tax_configuration_versions_are_immutable
                BEFORE INSERT OR UPDATE OR DELETE ON billing.tax_configuration_versions
                FOR EACH ROW EXECUTE FUNCTION billing.tax_configuration_versions_are_immutable();
                """);

            // 4. The codes and components of a published version: nothing may be inserted, changed
            // or deleted, whoever asks — and a row may not be moved between versions, so an UPDATE
            // is judged against the parent it leaves as well as the parent it joins. A code names
            // its version directly; a component reaches it through its code, which is why there are
            // two functions rather than one. A draft is freely editable, and a version already gone
            // is the cascade's business.
            migrationBuilder.Sql("""
                CREATE FUNCTION billing.published_tax_codes_are_immutable()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    old_status integer;
                    new_status integer;
                BEGIN
                    IF TG_OP <> 'INSERT' THEN
                        SELECT status INTO old_status
                        FROM billing.tax_configuration_versions
                        WHERE id = OLD.tax_configuration_version_id;
                    END IF;

                    IF TG_OP <> 'DELETE' THEN
                        SELECT status INTO new_status
                        FROM billing.tax_configuration_versions
                        WHERE id = NEW.tax_configuration_version_id;
                    END IF;

                    IF COALESCE(old_status, 0) = 0 AND COALESCE(new_status, 0) = 0 THEN
                        RETURN COALESCE(NEW, OLD);
                    END IF;

                    RAISE EXCEPTION
                        'billing.% belongs to a published or retired tax configuration version and '
                        'cannot change. Clone the version to a new draft and publish that.',
                        TG_TABLE_NAME
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER tax_codes_published_are_immutable
                BEFORE INSERT OR UPDATE OR DELETE ON billing.tax_codes
                FOR EACH ROW EXECUTE FUNCTION billing.published_tax_codes_are_immutable();
                """);

            migrationBuilder.Sql("""
                CREATE FUNCTION billing.published_tax_components_are_immutable()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    old_status integer;
                    new_status integer;
                BEGIN
                    IF TG_OP <> 'INSERT' THEN
                        SELECT v.status INTO old_status
                        FROM billing.tax_codes c
                        JOIN billing.tax_configuration_versions v ON v.id = c.tax_configuration_version_id
                        WHERE c.id = OLD.tax_code_id;
                    END IF;

                    IF TG_OP <> 'DELETE' THEN
                        SELECT v.status INTO new_status
                        FROM billing.tax_codes c
                        JOIN billing.tax_configuration_versions v ON v.id = c.tax_configuration_version_id
                        WHERE c.id = NEW.tax_code_id;
                    END IF;

                    IF COALESCE(old_status, 0) = 0 AND COALESCE(new_status, 0) = 0 THEN
                        RETURN COALESCE(NEW, OLD);
                    END IF;

                    RAISE EXCEPTION
                        'billing.tax_components belongs to a published or retired tax configuration '
                        'version and cannot change. Clone the version to a new draft and publish that.'
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER tax_components_published_are_immutable
                BEFORE INSERT OR UPDATE OR DELETE ON billing.tax_components
                FOR EACH ROW EXECUTE FUNCTION billing.published_tax_components_are_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The triggers go with their tables; the functions and the two constraints do not.
            migrationBuilder.Sql("ALTER TABLE billing.tax_configuration_versions DROP CONSTRAINT IF EXISTS ux_tax_configuration_versions_one_published;");
            migrationBuilder.Sql("ALTER TABLE billing.gst_registrations DROP CONSTRAINT IF EXISTS ex_gst_registrations_one_in_force;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS billing.tax_configuration_versions_are_immutable() CASCADE;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS billing.published_tax_codes_are_immutable() CASCADE;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS billing.published_tax_components_are_immutable() CASCADE;");

            migrationBuilder.DropTable(
                name: "gst_registrations",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "tax_components",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "tax_codes",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "tax_configuration_versions",
                schema: "billing");
        }
    }
}
