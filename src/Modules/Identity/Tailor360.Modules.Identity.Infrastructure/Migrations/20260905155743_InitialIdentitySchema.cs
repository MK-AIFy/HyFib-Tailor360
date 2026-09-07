using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentitySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "users",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    home_branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    normalised_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    mfa_enrolment = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    must_change_password = table.Column<bool>(type: "boolean", nullable: false),
                    failed_sign_in_count = table.Column<int>(type: "integer", nullable: false),
                    locked_out_until = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    last_sign_in_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    password_changed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "passkey_credentials",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_id = table.Column<byte[]>(type: "bytea", nullable: false),
                    public_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    authenticator_guid = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    transports = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    signature_counter = table.Column<long>(type: "bigint", nullable: false),
                    is_backup_eligible = table.Column<bool>(type: "boolean", nullable: false),
                    is_backed_up = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_passkey_credentials", x => x.id);
                    table.ForeignKey(
                        name: "fk_passkey_credentials_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recovery_codes",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recovery_codes", x => x.id);
                    table.CheckConstraint("ck_recovery_codes_code_hash_is_digest", "code_hash ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_recovery_codes_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organisation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    active_branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    device_label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    idle_expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    absolute_expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    last_strong_auth_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    mfa_satisfied = table.Column<bool>(type: "boolean", nullable: false),
                    trusted_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    end_reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    superseded_by_session_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                    table.CheckConstraint("ck_sessions_idle_within_absolute", "idle_expires_at <= absolute_expires_at");
                    table.CheckConstraint("ck_sessions_token_hash_is_digest", "token_hash ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_sessions_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "totp_enrolments",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    protected_secret = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    digits = table.Column<int>(type: "integer", nullable: false),
                    period_seconds = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    last_accepted_step = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_totp_enrolments", x => x.id);
                    table.ForeignKey(
                        name: "fk_totp_enrolments_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trusted_devices",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trusted_devices", x => x.id);
                    table.CheckConstraint("ck_trusted_devices_lifetime", "expires_at <= created_at + interval '30 days'");
                    table.CheckConstraint("ck_trusted_devices_token_hash_is_digest", "token_hash ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_trusted_devices_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_credentials",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    encoded_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    algorithm = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    last_verified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    rehash_required = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_credentials", x => x.id);
                    table.CheckConstraint("ck_user_credentials_encoded_hash_is_encoded", "char_length(encoded_hash) >= 20 AND encoded_hash !~ '\\s'");
                    table.ForeignKey(
                        name: "fk_user_credentials_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_preferences",
                schema: "identity",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    locale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    theme = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    density = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reduced_motion = table.Column<bool>(type: "boolean", nullable: false),
                    landing_route = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_preferences", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_user_preferences_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_passkey_credentials_user",
                schema: "identity",
                table: "passkey_credentials",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_passkey_credentials_credential_id",
                schema: "identity",
                table: "passkey_credentials",
                column: "credential_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recovery_codes_unspent",
                schema: "identity",
                table: "recovery_codes",
                column: "user_id",
                filter: "consumed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_recovery_codes_user_hash",
                schema: "identity",
                table: "recovery_codes",
                columns: new[] { "user_id", "code_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_absolute_expires_at",
                schema: "identity",
                table: "sessions",
                column: "absolute_expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_user_live",
                schema: "identity",
                table: "sessions",
                column: "user_id",
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_sessions_token_hash",
                schema: "identity",
                table: "sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_totp_enrolments_user",
                schema: "identity",
                table: "totp_enrolments",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trusted_devices_user_live",
                schema: "identity",
                table: "trusted_devices",
                column: "user_id",
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_trusted_devices_token_hash",
                schema: "identity",
                table: "trusted_devices",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_user_credentials_user",
                schema: "identity",
                table: "user_credentials",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_home_branch",
                schema: "identity",
                table: "users",
                column: "home_branch_id");

            migrationBuilder.CreateIndex(
                name: "ux_users_organisation_normalised_email",
                schema: "identity",
                table: "users",
                columns: new[] { "organisation_id", "normalised_email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_users_organisation_user_name",
                schema: "identity",
                table: "users",
                columns: new[] { "organisation_id", "user_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "passkey_credentials",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "recovery_codes",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "sessions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "totp_enrolments",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "trusted_devices",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_credentials",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_preferences",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "users",
                schema: "identity");
        }
    }
}
