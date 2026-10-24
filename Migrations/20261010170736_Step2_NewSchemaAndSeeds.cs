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

            // Preserve any reference rows that already exist (for example, rows created by an
            // earlier runtime seed). Insert only missing rows, and fail closed when an ID or a
            // business identity conflicts. Existing required flags and criterion values are
            // never overwritten.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [DOCUMENT_TYPES] WHERE [ID] = 1 AND [NAME] <> N'TitleDeed')
                BEGIN
                    THROW 51000, 'DOCUMENT_TYPES.ID = 1 exists with an unexpected name; refusing to change it.', 1;
                END

                IF EXISTS (SELECT 1 FROM [DOCUMENT_TYPES] WHERE [NAME] = N'TitleDeed' AND [ID] <> 1)
                BEGIN
                    THROW 51001, 'A TitleDeed document type already exists at another ID; refusing to create a conflicting row.', 1;
                END

                IF NOT EXISTS (SELECT 1 FROM [DOCUMENT_TYPES] WHERE [ID] = 1)
                BEGIN
                    SET IDENTITY_INSERT [DOCUMENT_TYPES] ON;
                    INSERT INTO [DOCUMENT_TYPES] ([ID], [IS_REQUIRED_FOR_VERIFICATION], [NAME])
                    VALUES (1, 1, N'TitleDeed');
                    SET IDENTITY_INSERT [DOCUMENT_TYPES] OFF;
                END

                IF EXISTS (SELECT 1 FROM [DOCUMENT_TYPES] WHERE [ID] = 2 AND [NAME] <> N'EncumbranceStatement')
                BEGIN
                    THROW 51002, 'DOCUMENT_TYPES.ID = 2 exists with an unexpected name; refusing to change it.', 1;
                END

                IF EXISTS (SELECT 1 FROM [DOCUMENT_TYPES] WHERE [NAME] = N'EncumbranceStatement' AND [ID] <> 2)
                BEGIN
                    THROW 51003, 'An EncumbranceStatement document type already exists at another ID; refusing to create a conflicting row.', 1;
                END

                IF NOT EXISTS (SELECT 1 FROM [DOCUMENT_TYPES] WHERE [ID] = 2)
                BEGIN
                    SET IDENTITY_INSERT [DOCUMENT_TYPES] ON;
                    INSERT INTO [DOCUMENT_TYPES] ([ID], [IS_REQUIRED_FOR_VERIFICATION], [NAME])
                    VALUES (2, 1, N'EncumbranceStatement');
                    SET IDENTITY_INSERT [DOCUMENT_TYPES] OFF;
                END
                """);

            migrationBuilder.Sql(
                """
                IF NOT EXISTS (SELECT 1 FROM [LAND_CRITERIA] WHERE [ID] = 1)
                BEGIN
                    IF EXISTS (SELECT 1 FROM [LAND_CRITERIA])
                    BEGIN
                        THROW 51004, 'LAND_CRITERIA has rows but no ID = 1; refusing to insert an older placeholder criterion.', 1;
                    END

                    SET IDENTITY_INSERT [LAND_CRITERIA] ON;
                    INSERT INTO [LAND_CRITERIA]
                        ([ID], [MIN_AREA_DONUM], [MAX_SLOPE_PCT], [MAX_GRID_DISTANCE_KM],
                         [MIN_SOLAR_IRRADIANCE], [MIN_ELEVATION_M], [REASON], [UPDATED_BY_ID])
                    VALUES
                        (1, 20.0, 10.0, 15.0, 5.0, NULL,
                         N'ILLUSTRATIVE PLACEHOLDER values for development; replace after engineering validation',
                         -1);
                    SET IDENTITY_INSERT [LAND_CRITERIA] OFF;
                END
                """);

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
                filter: "[IS_DELETED] = 0 AND [REGION_ID] IS NOT NULL AND [VILLAGE] IS NOT NULL AND [BASIN] IS NOT NULL AND [PARCEL_NUMBER] IS NOT NULL");

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
            // Rollback is restore-from-backup. This Down deletes the seed rows; on a
            // database where DOCUMENT_TYPES.ID = 1 or LAND_CRITERIA.ID = 1 already existed
            // before this migration, that would remove pre-existing reference data.
            // Do not run this Down against such a database.
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
