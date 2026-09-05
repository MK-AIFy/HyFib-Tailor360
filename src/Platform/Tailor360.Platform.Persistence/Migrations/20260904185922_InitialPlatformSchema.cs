using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlatformSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.CreateTable(
                name: "feature_flags",
                schema: "platform",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    scope_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000000")),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_flags", x => new { x.key, x.scope_type, x.scope_id });
                });

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                schema: "platform",
                columns: table => new
                {
                    principal_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    route = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    client_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    request_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    response_body = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_keys", x => new { x.principal_id, x.route, x.client_key });
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "platform",
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
                name: "job_leases",
                schema: "platform",
                columns: table => new
                {
                    job_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    owner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    acquired_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_leases", x => x.job_name);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "platform",
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
                name: "sequences",
                schema: "platform",
                columns: table => new
                {
                    sequence_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scope = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    next_value = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sequences", x => new { x.sequence_key, x.scope });
                });

            migrationBuilder.CreateTable(
                name: "worker_heartbeats",
                schema: "platform",
                columns: table => new
                {
                    instance_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    last_beat_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_worker_heartbeats", x => x.instance_name);
                });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_expires_at",
                schema: "platform",
                table: "idempotency_keys",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "platform",
                table: "outbox_messages",
                columns: new[] { "available_at", "aggregate_id" },
                filter: "processed_at IS NULL AND dead_lettered_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at",
                schema: "platform",
                table: "outbox_messages",
                column: "processed_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feature_flags",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "idempotency_keys",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "job_leases",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "sequences",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "worker_heartbeats",
                schema: "platform");
        }
    }
}
