using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NibrasWeb.Migrations
{
    /// <inheritdoc />
    public partial class Step2_NewSchemaAndSeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LANDS_REGION_ID",
                table: "LANDS");

            migrationBuilder.RenameIndex(
                name: "IX_GRID_CAPACITY_RESERVATIONS_CONTRACT_ID",
                table: "GRID_CAPACITY_RESERVATIONS",
                newName: "UQ_GRID_CAPACITY_RESERVATIONS_CONTRACT_ID");

            migrationBuilder.AddColumn<DateTime>(
                name: "APPROVED_AT",
                table: "USERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "APPROVED_BY_ID",
                table: "USERS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "COMPANY_NAME",
                table: "USERS",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "COMPANY_REGISTRATION_NUMBER",
                table: "USERS",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IS_APPROVED",
                table: "USERS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BASIN",
                table: "LANDS",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PARCEL_NUMBER",
                table: "LANDS",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VILLAGE",
                table: "LANDS",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "REVIEWED_AT",
                table: "LAND_DOCUMENTS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "REVIEWED_BY_ID",
                table: "LAND_DOCUMENTS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "REVIEW_NOTE",
                table: "LAND_DOCUMENTS",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "MIN_SOLAR_IRRADIANCE",
                table: "LAND_CRITERIA",
                type: "decimal(10,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(10,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "MIN_ELEVATION_M",
                table: "LAND_CRITERIA",
                type: "decimal(10,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(10,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "MIN_AREA_DONUM",
                table: "LAND_CRITERIA",
                type: "decimal(10,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(10,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "MAX_SLOPE_PCT",
                table: "LAND_CRITERIA",
                type: "decimal(5,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(5,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "MAX_GRID_DISTANCE_KM",
                table: "LAND_CRITERIA",
                type: "decimal(10,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(10,2)");

            migrationBuilder.AddColumn<string>(
                name: "REASON",
                table: "LAND_CRITERIA",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "RELEASED_AT",
                table: "GRID_CAPACITY_RESERVATIONS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RELEASE_REASON",
                table: "GRID_CAPACITY_RESERVATIONS",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IS_REQUIRED_FOR_VERIFICATION",
                table: "DOCUMENT_TYPES",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CRITERION_ID",
                table: "CONTRACTS",
                type: "int",
                nullable: true);

            migrationBuilder.InsertData(
                table: "DOCUMENT_TYPES",
                columns: new[] { "ID", "IS_REQUIRED_FOR_VERIFICATION", "NAME" },
                values: new object[,]
                {
                    { 1, true, "TitleDeed" },
                    { 2, true, "EncumbranceStatement" }
                });

            migrationBuilder.InsertData(
                table: "LAND_CRITERIA",
                columns: new[] { "ID", "MAX_GRID_DISTANCE_KM", "MAX_SLOPE_PCT", "MIN_AREA_DONUM", "MIN_ELEVATION_M", "MIN_SOLAR_IRRADIANCE", "REASON", "UPDATED_BY_ID" },
                values: new object[] { 1, 15m, 10m, 20m, null, 5.0m, "ILLUSTRATIVE PLACEHOLDER values for development; replace after engineering validation", -1 });

            migrationBuilder.UpdateData(
                table: "USERS",
                keyColumn: "ID",
                keyValue: -1,
                columns: new[] { "APPROVED_AT", "APPROVED_BY_ID", "COMPANY_NAME", "COMPANY_REGISTRATION_NUMBER" },
                values: new object[] { null, null, null, null });

            migrationBuilder.CreateIndex(
                name: "UQ_LAND_LOCATION",
                table: "LANDS",
                columns: new[] { "REGION_ID", "VILLAGE", "BASIN", "PARCEL_NUMBER" },
                unique: true,
                filter: "[IS_DELETED] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_LAND_DOCUMENTS_REVIEWED_BY_ID",
                table: "LAND_DOCUMENTS",
                column: "REVIEWED_BY_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACTS_CRITERION_ID",
                table: "CONTRACTS",
                column: "CRITERION_ID");

            migrationBuilder.AddForeignKey(
                name: "FK_CONTRACTS_CRITERIA",
                table: "CONTRACTS",
                column: "CRITERION_ID",
                principalTable: "LAND_CRITERIA",
                principalColumn: "ID");

            migrationBuilder.AddForeignKey(
                name: "FK_LD_REVIEWED_BY",
                table: "LAND_DOCUMENTS",
                column: "REVIEWED_BY_ID",
                principalTable: "USERS",
                principalColumn: "ID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CONTRACTS_CRITERIA",
                table: "CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_LD_REVIEWED_BY",
                table: "LAND_DOCUMENTS");

            migrationBuilder.DropIndex(
                name: "UQ_LAND_LOCATION",
                table: "LANDS");

            migrationBuilder.DropIndex(
                name: "IX_LAND_DOCUMENTS_REVIEWED_BY_ID",
                table: "LAND_DOCUMENTS");

            migrationBuilder.DropIndex(
                name: "IX_CONTRACTS_CRITERION_ID",
                table: "CONTRACTS");

            migrationBuilder.DeleteData(
                table: "DOCUMENT_TYPES",
                keyColumn: "ID",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "DOCUMENT_TYPES",
                keyColumn: "ID",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "LAND_CRITERIA",
                keyColumn: "ID",
                keyValue: 1);

            migrationBuilder.DropColumn(
                name: "APPROVED_AT",
                table: "USERS");

            migrationBuilder.DropColumn(
                name: "APPROVED_BY_ID",
                table: "USERS");

            migrationBuilder.DropColumn(
                name: "COMPANY_NAME",
                table: "USERS");

            migrationBuilder.DropColumn(
                name: "COMPANY_REGISTRATION_NUMBER",
                table: "USERS");

            migrationBuilder.DropColumn(
                name: "IS_APPROVED",
                table: "USERS");

            migrationBuilder.DropColumn(
                name: "BASIN",
                table: "LANDS");

            migrationBuilder.DropColumn(
                name: "PARCEL_NUMBER",
                table: "LANDS");

            migrationBuilder.DropColumn(
                name: "VILLAGE",
                table: "LANDS");

            migrationBuilder.DropColumn(
                name: "REVIEWED_AT",
                table: "LAND_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "REVIEWED_BY_ID",
                table: "LAND_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "REVIEW_NOTE",
                table: "LAND_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "REASON",
                table: "LAND_CRITERIA");

            migrationBuilder.DropColumn(
                name: "RELEASED_AT",
                table: "GRID_CAPACITY_RESERVATIONS");

            migrationBuilder.DropColumn(
                name: "RELEASE_REASON",
                table: "GRID_CAPACITY_RESERVATIONS");

            migrationBuilder.DropColumn(
                name: "IS_REQUIRED_FOR_VERIFICATION",
                table: "DOCUMENT_TYPES");

            migrationBuilder.DropColumn(
                name: "CRITERION_ID",
                table: "CONTRACTS");

            migrationBuilder.RenameIndex(
                name: "UQ_GRID_CAPACITY_RESERVATIONS_CONTRACT_ID",
                table: "GRID_CAPACITY_RESERVATIONS",
                newName: "IX_GRID_CAPACITY_RESERVATIONS_CONTRACT_ID");

            migrationBuilder.AlterColumn<decimal>(
                name: "MIN_SOLAR_IRRADIANCE",
                table: "LAND_CRITERIA",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(10,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "MIN_ELEVATION_M",
                table: "LAND_CRITERIA",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(10,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "MIN_AREA_DONUM",
                table: "LAND_CRITERIA",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(10,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "MAX_SLOPE_PCT",
                table: "LAND_CRITERIA",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(5,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "MAX_GRID_DISTANCE_KM",
                table: "LAND_CRITERIA",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(10,2)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LANDS_REGION_ID",
                table: "LANDS",
                column: "REGION_ID");
        }
    }
}
