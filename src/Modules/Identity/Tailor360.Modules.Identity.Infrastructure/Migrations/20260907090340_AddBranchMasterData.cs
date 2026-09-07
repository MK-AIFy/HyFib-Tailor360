using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailor360.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address_line1",
                schema: "identity",
                table: "branches",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address_line2",
                schema: "identity",
                table: "branches",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "city",
                schema: "identity",
                table: "branches",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_email",
                schema: "identity",
                table: "branches",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_phone",
                schema: "identity",
                table: "branches",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gst_registration_reference",
                schema: "identity",
                table: "branches",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "postal_code",
                schema: "identity",
                table: "branches",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "state",
                schema: "identity",
                table: "branches",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status_reason",
                schema: "identity",
                table: "branches",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "address_line1",
                schema: "identity",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "address_line2",
                schema: "identity",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "city",
                schema: "identity",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "contact_email",
                schema: "identity",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "contact_phone",
                schema: "identity",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "gst_registration_reference",
                schema: "identity",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "postal_code",
                schema: "identity",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "state",
                schema: "identity",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "status_reason",
                schema: "identity",
                table: "branches");
        }
    }
}
