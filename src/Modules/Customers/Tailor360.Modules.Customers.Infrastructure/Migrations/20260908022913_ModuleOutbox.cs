using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Customers.Infrastructure.Migrations
{
    /// <summary>
    /// Gives this module its own outbox and inbox, in its own schema.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tables are mapped by <c>ModuleDbContext</c> rather than by this module, so every module has
    /// them and none has to remember. What that buys is the only thing a transactional outbox is for:
    /// the module's own context writes the aggregate and the event, and one <c>SaveChangesAsync</c>
    /// commits both. Until issue #77 there was a single <c>platform.outbox_messages</c> on another
    /// context, which is a second connection and a second transaction — so one ordering committed work
    /// whose event was lost and the other announced work that rolled back.
    /// </para>
    /// <para>
    /// The inbox is here for the same reason read from the consuming end: a row that records a handler
    /// ran is worth something only if it commits with what the handler wrote.
    /// </para>
    /// <para>
    /// Additive. Nothing is dropped and nothing is moved: <c>platform.outbox_messages</c> and
    /// <c>platform.inbox_messages</c> keep their shape and their rows, and are now the platform
    /// module's own pair rather than everybody's.
    /// </para>
    /// </remarks>
    public partial class ModuleOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "customers",
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
                schema: "customers",
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

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "customers",
                table: "outbox_messages",
                columns: new[] { "available_at", "aggregate_id" },
                filter: "processed_at IS NULL AND dead_lettered_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at",
                schema: "customers",
                table: "outbox_messages",
                column: "processed_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "customers");
        }
    }
}
