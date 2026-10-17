using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NibrasWeb.Migrations
{
    /// <inheritdoc />
    public partial class DropRegulatoryTariffSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "REGULATORY_TARIFF_SETTINGS");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "REGULATORY_TARIFF_SETTINGS",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ANNUAL_LICENSE_FEE_PER_KWH_SELF = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    ANNUAL_LICENSE_FEE_PER_KWH_SOLD = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    ANNUAL_LICENSE_FEE_PER_MW = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    EFFECTIVE_FROM = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GRID_FEE_PER_KWAC_MONTH = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    IS_ACTIVE = table.Column<bool>(type: "bit", nullable: false),
                    IS_GRID_FEE_EXEMPT = table.Column<bool>(type: "bit", nullable: false),
                    MECHANISM = table.Column<int>(type: "int", nullable: false),
                    SALE_PRICE_OFF_PEAK = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    SALE_PRICE_PARTIAL_PEAK = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    SALE_PRICE_PEAK = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    SECTOR_CATEGORY = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SOURCE_REFERENCE = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SPECIFIC_YIELD_KWH_PER_KWP = table.Column<decimal>(type: "decimal(8,2)", nullable: false),
                    TAX_RATE_PCT = table.Column<decimal>(type: "decimal(5,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__REG_TARIFF_SETTINGS", x => x.ID);
                });

            migrationBuilder.InsertData(
                table: "REGULATORY_TARIFF_SETTINGS",
                columns: new[] { "ID", "ANNUAL_LICENSE_FEE_PER_KWH_SELF", "ANNUAL_LICENSE_FEE_PER_KWH_SOLD", "ANNUAL_LICENSE_FEE_PER_MW", "EFFECTIVE_FROM", "GRID_FEE_PER_KWAC_MONTH", "IS_ACTIVE", "IS_GRID_FEE_EXEMPT", "MECHANISM", "SALE_PRICE_OFF_PEAK", "SALE_PRICE_PARTIAL_PEAK", "SALE_PRICE_PEAK", "SECTOR_CATEGORY", "SOURCE_REFERENCE", "SPECIFIC_YIELD_KWH_PER_KWP", "TAX_RATE_PCT" },
                values: new object[,]
                {
                    { 1, 0m, 0.001m, 0m, new DateTime(2024, 7, 1, 0, 0, 0, 0, DateTimeKind.Utc), 13m, true, false, 1, 0.0705m, 0.0800m, 0.1000m, "Commercial", "Bylaw 58/2024 - Buy-All/Sell-All commercial", 1900m, 0m },
                    { 2, 0.001m, 0m, 0m, new DateTime(2024, 7, 1, 0, 0, 0, 0, DateTimeKind.Utc), 13m, true, false, 2, 0.0705m, 0.0800m, 0.1000m, "Commercial", "Bylaw 58/2024 - Self-Consumption Net Billing commercial", 1900m, 0m },
                    { 3, 0m, 0m, 0m, new DateTime(2024, 7, 1, 0, 0, 0, 0, DateTimeKind.Utc), 13m, true, false, 3, 0m, 0m, 0m, "Commercial", "Bylaw 58/2024 - Zero-Export commercial", 1900m, 0m },
                    { 4, 0m, 0m, 0m, new DateTime(2024, 7, 1, 0, 0, 0, 0, DateTimeKind.Utc), 13m, true, false, 4, 0.0705m, 0.0705m, 0.0705m, "Commercial", "Bylaw 58/2024 - Full-Netting commercial", 1900m, 0m }
                });
        }
    }
}
