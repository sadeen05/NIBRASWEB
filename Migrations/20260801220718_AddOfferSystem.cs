using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NibrasWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddOfferSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TARIFF_BRACKETS");

            migrationBuilder.DropIndex(
                name: "UQ__GRID_CAP__3F5DFF15DEC96C5D",
                table: "GRID_CAPACITY_RESERVATIONS");

            migrationBuilder.RenameColumn(
                name: "LastProposedById",
                table: "OFFERS",
                newName: "LAST_PROPOSED_BY_ID");

            migrationBuilder.RenameColumn(
                name: "CurrentAmount",
                table: "OFFERS",
                newName: "CURRENT_AMOUNT");

            migrationBuilder.RenameColumn(
                name: "apdateAt",
                table: "OFFERS",
                newName: "UPDATED_AT");

            migrationBuilder.AlterColumn<decimal>(
                name: "CURRENT_AMOUNT",
                table: "OFFERS",
                type: "decimal(14,3)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AddColumn<int>(
                name: "CONNECTION_MECHANISM",
                table: "OFFERS",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RELATED_TO_REJECTED_OFFER_ID",
                table: "OFFERS",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(@"
UPDATE OFFERS SET CONNECTION_MECHANISM = 1 WHERE CONNECTION_MECHANISM = 0;");

            migrationBuilder.AddColumn<string>(
                name: "COMPLIANCE_ISSUES",
                table: "OFFER_VERSIONS",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

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

            migrationBuilder.AddColumn<bool>(
                name: "IS_COMPLIANT",
                table: "OFFER_VERSIONS",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LICENSE_FEES",
                table: "OFFER_VERSIONS",
                type: "decimal(14,3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MESSAGE",
                table: "OFFER_VERSIONS",
                type: "nvarchar(500)",
                maxLength: 500,
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

            migrationBuilder.AddColumn<int>(
                name: "GRID_ID",
                table: "LANDS",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM LANDS WHERE GRID_ID = 0)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM GRIDS)
    BEGIN
        DECLARE @RegionId int = (SELECT TOP 1 ID FROM REGIONS ORDER BY ID);
        INSERT INTO GRIDS (REGION_ID, NAME, CAPACITY_MW, STATUS)
        VALUES (@RegionId, N'Backfill Grid', 100.0, 'Active');
        DECLARE @GridId int = SCOPE_IDENTITY();
        UPDATE LANDS SET GRID_ID = @GridId WHERE GRID_ID = 0;
    END
    ELSE
    BEGIN
        DECLARE @AnyGridId int = (SELECT TOP 1 ID FROM GRIDS ORDER BY ID);
        UPDATE LANDS SET GRID_ID = @AnyGridId WHERE GRID_ID = 0;
    END
END");

            migrationBuilder.AlterColumn<int>(
                name: "CONTRACT_ID",
                table: "GRID_CAPACITY_RESERVATIONS",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "OFFER_ID",
                table: "GRID_CAPACITY_RESERVATIONS",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RESERVATION_TYPE",
                table: "GRID_CAPACITY_RESERVATIONS",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OFFER_NEGOTIATION_HISTORY",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OFFER_ID = table.Column<int>(type: "int", nullable: false),
                    ACTION_TYPE = table.Column<int>(type: "int", nullable: false),
                    ACTOR_ID = table.Column<int>(type: "int", nullable: false),
                    AMOUNT = table.Column<decimal>(type: "decimal(14,3)", nullable: true),
                    MESSAGE = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__OFFER_NEG_HISTORY", x => x.ID);
                    table.ForeignKey(
                        name: "FK_ONH_OFFERS",
                        column: x => x.OFFER_ID,
                        principalTable: "OFFERS",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_ONH_USERS",
                        column: x => x.ACTOR_ID,
                        principalTable: "USERS",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "REGULATORY_TARIFF_SETTINGS",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MECHANISM = table.Column<int>(type: "int", nullable: false),
                    SECTOR_CATEGORY = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SPECIFIC_YIELD_KWH_PER_KWP = table.Column<decimal>(type: "decimal(8,2)", nullable: false),
                    SALE_PRICE_OFF_PEAK = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    SALE_PRICE_PEAK = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    SALE_PRICE_PARTIAL_PEAK = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    GRID_FEE_PER_KWAC_MONTH = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    IS_GRID_FEE_EXEMPT = table.Column<bool>(type: "bit", nullable: false),
                    ANNUAL_LICENSE_FEE_PER_KWH_SOLD = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    ANNUAL_LICENSE_FEE_PER_KWH_SELF = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    ANNUAL_LICENSE_FEE_PER_MW = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    TAX_RATE_PCT = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    EFFECTIVE_FROM = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SOURCE_REFERENCE = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IS_ACTIVE = table.Column<bool>(type: "bit", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_OFFERS_RELATED_TO_REJECTED_OFFER_ID",
                table: "OFFERS",
                column: "RELATED_TO_REJECTED_OFFER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_LANDS_GRID_ID",
                table: "LANDS",
                column: "GRID_ID");

            migrationBuilder.CreateIndex(
                name: "IX_GRID_CAPACITY_RESERVATIONS_CONTRACT_ID",
                table: "GRID_CAPACITY_RESERVATIONS",
                column: "CONTRACT_ID",
                unique: true,
                filter: "[CONTRACT_ID] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UQ__GRID_CAP__OFFER_ID",
                table: "GRID_CAPACITY_RESERVATIONS",
                column: "OFFER_ID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OFFER_NEGOTIATION_HISTORY_ACTOR_ID",
                table: "OFFER_NEGOTIATION_HISTORY",
                column: "ACTOR_ID");

            migrationBuilder.CreateIndex(
                name: "IX_OFFER_NEGOTIATION_HISTORY_OFFER_ID",
                table: "OFFER_NEGOTIATION_HISTORY",
                column: "OFFER_ID");

            migrationBuilder.AddForeignKey(
                name: "FK_GCR_OFFERS",
                table: "GRID_CAPACITY_RESERVATIONS",
                column: "OFFER_ID",
                principalTable: "OFFERS",
                principalColumn: "ID");

            migrationBuilder.AddForeignKey(
                name: "FK_LANDS_GRIDS",
                table: "LANDS",
                column: "GRID_ID",
                principalTable: "GRIDS",
                principalColumn: "ID");

            migrationBuilder.AddForeignKey(
                name: "FK_OFFERS_RELATED_REJECTED",
                table: "OFFERS",
                column: "RELATED_TO_REJECTED_OFFER_ID",
                principalTable: "OFFERS",
                principalColumn: "ID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GCR_OFFERS",
                table: "GRID_CAPACITY_RESERVATIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_LANDS_GRIDS",
                table: "LANDS");

            migrationBuilder.DropForeignKey(
                name: "FK_OFFERS_RELATED_REJECTED",
                table: "OFFERS");

            migrationBuilder.DropTable(
                name: "OFFER_NEGOTIATION_HISTORY");

            migrationBuilder.DropTable(
                name: "REGULATORY_TARIFF_SETTINGS");

            migrationBuilder.DropIndex(
                name: "IX_OFFERS_RELATED_TO_REJECTED_OFFER_ID",
                table: "OFFERS");

            migrationBuilder.DropIndex(
                name: "IX_LANDS_GRID_ID",
                table: "LANDS");

            migrationBuilder.DropIndex(
                name: "IX_GRID_CAPACITY_RESERVATIONS_CONTRACT_ID",
                table: "GRID_CAPACITY_RESERVATIONS");

            migrationBuilder.DropIndex(
                name: "UQ__GRID_CAP__OFFER_ID",
                table: "GRID_CAPACITY_RESERVATIONS");

            migrationBuilder.DropColumn(
                name: "CONNECTION_MECHANISM",
                table: "OFFERS");

            migrationBuilder.DropColumn(
                name: "RELATED_TO_REJECTED_OFFER_ID",
                table: "OFFERS");

            migrationBuilder.DropColumn(
                name: "COMPLIANCE_ISSUES",
                table: "OFFER_VERSIONS");

            migrationBuilder.DropColumn(
                name: "GRID_FEES",
                table: "OFFER_VERSIONS");

            migrationBuilder.DropColumn(
                name: "GROSS_REVENUE",
                table: "OFFER_VERSIONS");

            migrationBuilder.DropColumn(
                name: "IS_COMPLIANT",
                table: "OFFER_VERSIONS");

            migrationBuilder.DropColumn(
                name: "LICENSE_FEES",
                table: "OFFER_VERSIONS");

            migrationBuilder.DropColumn(
                name: "MESSAGE",
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

            migrationBuilder.DropColumn(
                name: "GRID_ID",
                table: "LANDS");

            migrationBuilder.DropColumn(
                name: "OFFER_ID",
                table: "GRID_CAPACITY_RESERVATIONS");

            migrationBuilder.DropColumn(
                name: "RESERVATION_TYPE",
                table: "GRID_CAPACITY_RESERVATIONS");

            migrationBuilder.RenameColumn(
                name: "LAST_PROPOSED_BY_ID",
                table: "OFFERS",
                newName: "LastProposedById");

            migrationBuilder.RenameColumn(
                name: "CURRENT_AMOUNT",
                table: "OFFERS",
                newName: "CurrentAmount");

            migrationBuilder.RenameColumn(
                name: "UPDATED_AT",
                table: "OFFERS",
                newName: "apdateAt");

            migrationBuilder.AlterColumn<decimal>(
                name: "CurrentAmount",
                table: "OFFERS",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(14,3)");

            migrationBuilder.AlterColumn<int>(
                name: "CONTRACT_ID",
                table: "GRID_CAPACITY_RESERVATIONS",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "TARIFF_BRACKETS",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    REGION_ID = table.Column<int>(type: "int", nullable: false),
                    EFFECTIVE_FROM = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EFFECTIVE_TO = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FROM_KWH = table.Column<int>(type: "int", nullable: false),
                    RATE_PER_KWH = table.Column<decimal>(type: "decimal(10,3)", nullable: false),
                    TO_KWH = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__TARIFF_BRACKETS", x => x.ID);
                    table.ForeignKey(
                        name: "FK_TB_REGIONS",
                        column: x => x.REGION_ID,
                        principalTable: "REGIONS",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateIndex(
                name: "UQ__GRID_CAP__3F5DFF15DEC96C5D",
                table: "GRID_CAPACITY_RESERVATIONS",
                column: "CONTRACT_ID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TARIFF_BRACKETS_REGION_ID",
                table: "TARIFF_BRACKETS",
                column: "REGION_ID");
        }
    }
}
