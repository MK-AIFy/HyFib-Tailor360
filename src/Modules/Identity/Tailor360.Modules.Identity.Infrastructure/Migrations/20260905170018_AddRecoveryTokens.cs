using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecoveryTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recovery_tokens",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    purpose = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    invalidated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recovery_tokens", x => x.id);
                    table.CheckConstraint("ck_recovery_tokens_lifetime", "expires_at <= created_at + interval '60 minutes'");
                    table.CheckConstraint("ck_recovery_tokens_token_hash_is_digest", "token_hash ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_recovery_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recovery_tokens_expires_at",
                schema: "identity",
                table: "recovery_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_recovery_tokens_outstanding",
                schema: "identity",
                table: "recovery_tokens",
                columns: new[] { "user_id", "purpose" },
                filter: "consumed_at IS NULL AND invalidated_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_recovery_tokens_token_hash",
                schema: "identity",
                table: "recovery_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recovery_tokens",
                schema: "identity");
        }
    }
}
