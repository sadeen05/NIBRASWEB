using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NibrasWeb.Migrations
{
    /// <inheritdoc />
    public partial class DropOfferFinancialColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GRID_FEES",
                table: "OFFER_VERSIONS");

            migrationBuilder.DropColumn(
                name: "GROSS_REVENUE",
                table: "OFFER_VERSIONS");

            migrationBuilder.DropColumn(
                name: "LICENSE_FEES",
                table: "OFFER_VERSIONS");

            migrationBuilder.DropColumn(
                name: "NET_REVENUE",
                table: "OFFER_VERSIONS");

            migrationBuilder.DropColumn(
                name: "TARIFF_SOURCE_REFERENCE",
                table: "OFFER_VERSIONS");

            migrationBuilder.DropColumn(
                name: "TAX_AMOUNT",
                table: "OFFER_VERSIONS");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "GRID_FEES",
                table: "OFFER_VERSIONS",
                type: "decimal(14,3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GROSS_REVENUE",
                table: "OFFER_VERSIONS",
                type: "decimal(14,3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LICENSE_FEES",
                table: "OFFER_VERSIONS",
                type: "decimal(14,3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NET_REVENUE",
                table: "OFFER_VERSIONS",
                type: "decimal(14,3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TARIFF_SOURCE_REFERENCE",
                table: "OFFER_VERSIONS",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TAX_AMOUNT",
                table: "OFFER_VERSIONS",
                type: "decimal(14,3)",
                nullable: true);
        }
    }
}
