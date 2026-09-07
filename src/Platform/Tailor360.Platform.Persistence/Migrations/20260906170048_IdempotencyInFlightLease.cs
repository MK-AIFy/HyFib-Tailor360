using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdempotencyInFlightLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "in_flight_until",
                schema: "platform",
                table: "idempotency_keys",
                type: "timestamptz",
                nullable: true);

            // Rows written before this column existed carry no lease, and the claim statement only takes a
            // claim over when one has run out. Dating them to their creation makes every one of them
            // immediately reclaimable: they are from a process that is no longer running, so the only
            // alternative is a key nobody can ever use again.
            migrationBuilder.Sql(
                """
                UPDATE platform.idempotency_keys
                   SET in_flight_until = created_at
                 WHERE status = 'in_progress' AND in_flight_until IS NULL
                """);

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_in_flight",
                schema: "platform",
                table: "idempotency_keys",
                column: "in_flight_until",
                filter: "status = 'in_progress'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_idempotency_keys_in_flight",
                schema: "platform",
                table: "idempotency_keys");

            migrationBuilder.DropColumn(
                name: "in_flight_until",
                schema: "platform",
                table: "idempotency_keys");
        }
    }
}
