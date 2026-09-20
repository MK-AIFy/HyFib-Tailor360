using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Media.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialMediaSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "media");

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "media",
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
                name: "media_access_log",
                schema: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accessed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    accessed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    variant = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_access_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "media_objects",
                schema: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<int>(type: "integer", nullable: false),
                    classification = table.Column<int>(type: "integer", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    consent_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    object_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    checksum = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    alt_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    retention_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_objects", x => x.id);
                    table.CheckConstraint("ck_media_objects_alt_text_required_for_diagrams", "purpose NOT IN (2, 3) OR alt_text IS NOT NULL");
                    table.CheckConstraint("ck_media_objects_checksum_is_well_formed", "checksum ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_media_objects_size_is_positive", "size_bytes > 0");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "media",
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
                name: "media_derivatives",
                schema: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant = table.Column<int>(type: "integer", nullable: false),
                    object_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    width_px = table.Column<int>(type: "integer", nullable: false),
                    height_px = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_derivatives", x => x.id);
                    table.CheckConstraint("ck_media_derivatives_dimensions_are_positive", "width_px > 0 AND height_px > 0");
                    table.CheckConstraint("ck_media_derivatives_size_is_positive", "size_bytes > 0");
                    table.ForeignKey(
                        name: "fk_media_derivatives_media_objects_media_object_id",
                        column: x => x.media_object_id,
                        principalSchema: "media",
                        principalTable: "media_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "media_quarantine",
                schema: "media",
                columns: table => new
                {
                    media_object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quarantine_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    scan_outcome = table.Column<int>(type: "integer", nullable: true),
                    scanned_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_quarantine", x => x.media_object_id);
                    table.CheckConstraint("ck_media_quarantine_attempt_count_is_not_negative", "attempt_count >= 0");
                    table.ForeignKey(
                        name: "fk_media_quarantine_media_objects_media_object_id",
                        column: x => x.media_object_id,
                        principalSchema: "media",
                        principalTable: "media_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "media_retention_holds",
                schema: "media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    placed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    placed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    released_by = table.Column<Guid>(type: "uuid", nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_retention_holds", x => x.id);
                    table.CheckConstraint("ck_media_retention_holds_release_is_ordered", "released_at IS NULL OR released_at >= placed_at");
                    table.ForeignKey(
                        name: "fk_media_retention_holds_media_objects_media_object_id",
                        column: x => x.media_object_id,
                        principalSchema: "media",
                        principalTable: "media_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_media_access_log_object_accessed",
                schema: "media",
                table: "media_access_log",
                columns: new[] { "media_object_id", "accessed_at" });

            migrationBuilder.CreateIndex(
                name: "ux_media_derivatives_object_key",
                schema: "media",
                table: "media_derivatives",
                column: "object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_media_derivatives_object_variant",
                schema: "media",
                table: "media_derivatives",
                columns: new[] { "media_object_id", "variant" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_media_objects_customer",
                schema: "media",
                table: "media_objects",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_objects_job",
                schema: "media",
                table: "media_objects",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_objects_organisation_status",
                schema: "media",
                table: "media_objects",
                columns: new[] { "organisation_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_media_objects_retention_date",
                schema: "media",
                table: "media_objects",
                column: "retention_date");

            migrationBuilder.CreateIndex(
                name: "ux_media_objects_object_key",
                schema: "media",
                table: "media_objects",
                column: "object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_media_retention_holds_object_released",
                schema: "media",
                table: "media_retention_holds",
                columns: new[] { "media_object_id", "released_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "media",
                table: "outbox_messages",
                columns: new[] { "available_at", "aggregate_id" },
                filter: "processed_at IS NULL AND dead_lettered_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at",
                schema: "media",
                table: "outbox_messages",
                column: "processed_at");

            // media_access_log is append-only: who streamed which object, when, is evidence, and
            // evidence that could be edited after the fact is not evidence. The same shape
            // billing.financial_records_are_append_only uses — one function per schema, one trigger
            // per append-only table.
            migrationBuilder.Sql("""
                CREATE FUNCTION media.records_are_append_only()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION
                        'media.% is append-only: a logged access is never changed or deleted.',
                        TG_TABLE_NAME
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER media_access_log_is_append_only
                BEFORE UPDATE OR DELETE ON media.media_access_log
                FOR EACH ROW EXECUTE FUNCTION media.records_are_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS media_access_log_is_append_only ON media.media_access_log;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS media.records_are_append_only() CASCADE;");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "media");

            migrationBuilder.DropTable(
                name: "media_access_log",
                schema: "media");

            migrationBuilder.DropTable(
                name: "media_derivatives",
                schema: "media");

            migrationBuilder.DropTable(
                name: "media_quarantine",
                schema: "media");

            migrationBuilder.DropTable(
                name: "media_retention_holds",
                schema: "media");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "media");

            migrationBuilder.DropTable(
                name: "media_objects",
                schema: "media");
        }
    }
}
