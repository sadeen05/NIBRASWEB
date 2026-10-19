using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NibrasWeb.Migrations
{
    /// <inheritdoc />
    public partial class Phase1_GovernoratesAndLandAssessment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_LAND_LOCATION",
                table: "LANDS");

            migrationBuilder.DropIndex(
                name: "UQ_LAND_NUMBER_REGION",
                table: "LANDS");

            migrationBuilder.AlterColumn<decimal>(
                name: "SOLAR_IRRADIANCE",
                table: "LANDS",
                type: "decimal(10,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(10,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "SLOPE_PERCENTAGE",
                table: "LANDS",
                type: "decimal(5,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(5,2)");

            migrationBuilder.AlterColumn<int>(
                name: "REGION_ID",
                table: "LANDS",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            // Phase 1 hardening (not emitted by the generator): IX_LANDS_GRID_ID is a key
            // index on GRID_ID and is retained across the nullability change below. EF's own
            // Down() for this migration drops and recreates the same index around the same
            // column alteration, so the Up does it explicitly too. Index-only, no data loss.
            migrationBuilder.DropIndex(
                name: "IX_LANDS_GRID_ID",
                table: "LANDS");

            migrationBuilder.AlterColumn<int>(
                name: "GRID_ID",
                table: "LANDS",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateIndex(
                name: "IX_LANDS_GRID_ID",
                table: "LANDS",
                column: "GRID_ID");

            migrationBuilder.AlterColumn<decimal>(
                name: "ELEVATION_M",
                table: "LANDS",
                type: "decimal(10,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(10,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "DISTANCE_TO_GRID_KM",
                table: "LANDS",
                type: "decimal(10,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(10,2)");

            migrationBuilder.AddColumn<string>(
                name: "DESCRIPTION",
                table: "LANDS",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GOVERNORATE_ID",
                table: "LANDS",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GOVERNORATES",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CODE = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    NAME_EN = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NAME_AR = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GOVERNORATES", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "LAND_ASSESSMENTS",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LAND_ID = table.Column<int>(type: "int", nullable: false),
                    ASSESSED_BY_ID = table.Column<int>(type: "int", nullable: false),
                    ASSESSED_AT = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NOTE = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    REGION_ID = table.Column<int>(type: "int", nullable: true),
                    GRID_ID = table.Column<int>(type: "int", nullable: true),
                    SLOPE_PERCENTAGE = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    DISTANCE_TO_GRID_KM = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    SOLAR_IRRADIANCE = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    ELEVATION_M = table.Column<decimal>(type: "decimal(10,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LAND_ASSESSMENTS", x => x.ID);
                    table.ForeignKey(
                        name: "FK_LA_LANDS",
                        column: x => x.LAND_ID,
                        principalTable: "LANDS",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_LA_USERS",
                        column: x => x.ASSESSED_BY_ID,
                        principalTable: "USERS",
                        principalColumn: "ID");
                });

            migrationBuilder.InsertData(
                table: "GOVERNORATES",
                columns: new[] { "ID", "CODE", "NAME_AR", "NAME_EN" },
                values: new object[,]
                {
                    { 1, "AM", "عمّان", "Amman" },
                    { 2, "IR", "إربد", "Irbid" },
                    { 3, "AZ", "الزرقاء", "Zarqa" },
                    { 4, "BA", "البلقاء", "Balqa" },
                    { 5, "MA", "المفرق", "Mafraq" },
                    { 6, "KA", "الكرك", "Karak" },
                    { 7, "JA", "جرش", "Jerash" },
                    { 8, "MD", "مادبا", "Madaba" },
                    { 9, "AJ", "عجلون", "Ajloun" },
                    { 10, "AQ", "العقبة", "Aqaba" },
                    { 11, "MN", "معان", "Ma'an" },
                    { 12, "AT", "الطفيلة", "Tafilah" }
                });

            migrationBuilder.CreateIndex(
                name: "UQ_LAND_LOCATION",
                table: "LANDS",
                columns: new[] { "REGION_ID", "VILLAGE", "BASIN", "PARCEL_NUMBER" },
                unique: true,
                filter: "[IS_DELETED] = 0 AND [REGION_ID] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UQ_LAND_LOCATION_GOV",
                table: "LANDS",
                columns: new[] { "GOVERNORATE_ID", "VILLAGE", "BASIN", "PARCEL_NUMBER" },
                unique: true,
                filter: "[IS_DELETED] = 0 AND [GOVERNORATE_ID] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UQ_LAND_NUMBER_GOV",
                table: "LANDS",
                columns: new[] { "LAND_NUMBER", "GOVERNORATE_ID" },
                unique: true,
                filter: "[GOVERNORATE_ID] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UQ_LAND_NUMBER_REGION",
                table: "LANDS",
                columns: new[] { "LAND_NUMBER", "REGION_ID" },
                unique: true,
                filter: "[REGION_ID] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UQ_GOVERNORATES_CODE",
                table: "GOVERNORATES",
                column: "CODE",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LAND_ASSESSMENTS_ASSESSED_BY_ID",
                table: "LAND_ASSESSMENTS",
                column: "ASSESSED_BY_ID");

            migrationBuilder.CreateIndex(
                name: "IX_LAND_ASSESSMENTS_LAND_ID",
                table: "LAND_ASSESSMENTS",
                column: "LAND_ID");

            migrationBuilder.AddForeignKey(
                name: "FK_LANDS_GOVERNORATES",
                table: "LANDS",
                column: "GOVERNORATE_ID",
                principalTable: "GOVERNORATES",
                principalColumn: "ID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LANDS_GOVERNORATES",
                table: "LANDS");

            migrationBuilder.DropTable(
                name: "GOVERNORATES");

            migrationBuilder.DropTable(
                name: "LAND_ASSESSMENTS");

            migrationBuilder.DropIndex(
                name: "UQ_LAND_LOCATION",
                table: "LANDS");

            migrationBuilder.DropIndex(
                name: "UQ_LAND_LOCATION_GOV",
                table: "LANDS");

            migrationBuilder.DropIndex(
                name: "UQ_LAND_NUMBER_GOV",
                table: "LANDS");

            migrationBuilder.DropIndex(
                name: "UQ_LAND_NUMBER_REGION",
                table: "LANDS");

            migrationBuilder.DropColumn(
                name: "DESCRIPTION",
                table: "LANDS");

            migrationBuilder.DropColumn(
                name: "GOVERNORATE_ID",
                table: "LANDS");

            // Deliberately NO `defaultValue` on these six alterations. A generated rollback would
            // have emitted `UPDATE [LANDS] SET x = 0 WHERE x IS NULL` plus `ADD DEFAULT 0`, i.e. it
            // would fabricate technical measurements (and an invalid REGION_ID/GRID_ID of 0) and
            // conceal the missing data. Without a default, SQL Server fails loudly if any row still
            // holds NULL in one of these columns, which is the intended, honest behaviour.
            // Recovery is forward-fix-first (see docs/SECURITY_TRACEABILITY.md 3.2): restore a
            // backup or backfill real values in a reviewed migration; never run this Down against a
            // database that contains governorate-only lands.
            migrationBuilder.AlterColumn<decimal>(
                name: "SOLAR_IRRADIANCE",
                table: "LANDS",
                type: "decimal(10,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(10,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "SLOPE_PERCENTAGE",
                table: "LANDS",
                type: "decimal(5,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(5,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "REGION_ID",
                table: "LANDS",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "GRID_ID",
                table: "LANDS",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "ELEVATION_M",
                table: "LANDS",
                type: "decimal(10,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(10,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "DISTANCE_TO_GRID_KM",
                table: "LANDS",
                type: "decimal(10,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(10,2)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "UQ_LAND_LOCATION",
                table: "LANDS",
                columns: new[] { "REGION_ID", "VILLAGE", "BASIN", "PARCEL_NUMBER" },
                unique: true,
                filter: "[IS_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "UQ_LAND_NUMBER_REGION",
                table: "LANDS",
                columns: new[] { "LAND_NUMBER", "REGION_ID" },
                unique: true);
        }
    }
}
