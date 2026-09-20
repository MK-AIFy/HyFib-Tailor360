using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPreferenceTextSize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The default is the enum member name, not the scaffolder's empty string — an empty string
            // parses as no member at all, exactly the correction 20260905193715_AddSessionPendingStep
            // already made for the same reason. Every row that exists gets 'Standard' without a
            // backfill pass, which is what makes this expand-only.
            migrationBuilder.AddColumn<string>(
                name: "text_size",
                schema: "identity",
                table: "user_preferences",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Standard");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "text_size",
                schema: "identity",
                table: "user_preferences");
        }
    }
}
