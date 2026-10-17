using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NibrasWeb.Migrations
{
    /// <inheritdoc />
    public partial class DropAssessmentColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "COMPLIANCE_ISSUES",
                table: "OFFER_VERSIONS");

            migrationBuilder.DropColumn(
                name: "IS_COMPLIANT",
                table: "OFFER_VERSIONS");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "COMPLIANCE_ISSUES",
                table: "OFFER_VERSIONS",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IS_COMPLIANT",
                table: "OFFER_VERSIONS",
                type: "bit",
                nullable: true);
        }
    }
}
