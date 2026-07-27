using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NibrasWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddCancellationAndTariffSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "ROLES");

            migrationBuilder.AddColumn<decimal>(
                name: "LOSS_PERCENTAGE",
                table: "REGIONS",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PEAK_SUN_HOURS_PER_DAY",
                table: "REGIONS",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 5.5m);

            migrationBuilder.AddColumn<decimal>(
                name: "WHEELING_FEE_PER_KWH",
                table: "REGIONS",
                type: "decimal(10,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ACCEPTED_VERSION_ID",
                table: "OFFERS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EFFECTIVE_COST_PER_KWH",
                table: "OFFER_VERSIONS",
                type: "decimal(10,3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EXPECTED_ANNUAL_REVENUE",
                table: "OFFER_VERSIONS",
                type: "decimal(14,3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PAYBACK_PERIOD_MONTHS",
                table: "OFFER_VERSIONS",
                type: "decimal(8,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SOLAR_CELL_CAPACITY_KW",
                table: "OFFER_VERSIONS",
                type: "decimal(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DOCUMENT_STORAGE_LOCATION",
                table: "LANDS",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VERIFIED_AGAINST_CRITERION_ID",
                table: "LANDS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CANCELLATION_EFFECTIVE_DATE",
                table: "CONTRACTS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CANCELLATION_REQUESTED_AT",
                table: "CONTRACTS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CANCELLATION_REQUESTED_BY_ID",
                table: "CONTRACTS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "COMPENSATION_AMOUNT",
                table: "CONTRACTS",
                type: "decimal(14,3)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DISPUTE_FLAGGED",
                table: "CONTRACTS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "INVESTOR_PENALTY_AMOUNT",
                table: "CONTRACTS",
                type: "decimal(14,3)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NOTICE_PERIOD_DAYS",
                table: "CONTRACTS",
                type: "int",
                nullable: false,
                defaultValue: 90);

            migrationBuilder.AddColumn<int>(
                name: "OFFER_VERSION_ID",
                table: "CONTRACTS",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TARIFF_BRACKETS",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    REGION_ID = table.Column<int>(type: "int", nullable: false),
                    FROM_KWH = table.Column<int>(type: "int", nullable: false),
                    TO_KWH = table.Column<int>(type: "int", nullable: true),
                    RATE_PER_KWH = table.Column<decimal>(type: "decimal(10,3)", nullable: false),
                    EFFECTIVE_FROM = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EFFECTIVE_TO = table.Column<DateTime>(type: "datetime2", nullable: true)
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

            migrationBuilder.InsertData(
                table: "CONTRACT_STATUSES",
                columns: new[] { "ID", "NAME" },
                values: new object[,]
                {
                    { 1, "PendingSignatures" },
                    { 2, "Active" },
                    { 3, "Terminated" },
                    { 4, "Rejected" }
                });

            migrationBuilder.InsertData(
                table: "LAND_STATUSES",
                columns: new[] { "ID", "NAME" },
                values: new object[,]
                {
                    { 1, "Draft" },
                    { 2, "PendingVerification" },
                    { 3, "Verified" },
                    { 4, "Rejected" }
                });

            migrationBuilder.InsertData(
                table: "ROLES",
                columns: new[] { "ID", "NAME" },
                values: new object[,]
                {
                    { 1, "Landlord" },
                    { 2, "Investor" },
                    { 3, "Admin" },
                    { 4, "SuperAdmin" }
                });

            migrationBuilder.InsertData(
                table: "USERS",
                columns: new[] { "ID", "CREATED_AT", "DatefoBirth", "EMAIL", "FULL_NAME", "IS_DELETED", "PASSWORD_HASH", "PHONE", "ROLE_ID" },
                values: new object[] { -1, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "system@nibras.internal", "System", false, "N/A", "0000000000", 4 });

            migrationBuilder.CreateIndex(
                name: "IX_OFFERS_ACCEPTED_VERSION_ID",
                table: "OFFERS",
                column: "ACCEPTED_VERSION_ID");

            migrationBuilder.CreateIndex(
                name: "IX_LANDS_VERIFIED_AGAINST_CRITERION_ID",
                table: "LANDS",
                column: "VERIFIED_AGAINST_CRITERION_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACTS_CANCELLATION_REQUESTED_BY_ID",
                table: "CONTRACTS",
                column: "CANCELLATION_REQUESTED_BY_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACTS_OFFER_VERSION_ID",
                table: "CONTRACTS",
                column: "OFFER_VERSION_ID");

            migrationBuilder.CreateIndex(
                name: "IX_TARIFF_BRACKETS_REGION_ID",
                table: "TARIFF_BRACKETS",
                column: "REGION_ID");

            migrationBuilder.AddForeignKey(
                name: "FK_CONTRACTS_CANCELLATION_REQUESTER",
                table: "CONTRACTS",
                column: "CANCELLATION_REQUESTED_BY_ID",
                principalTable: "USERS",
                principalColumn: "ID");

            migrationBuilder.AddForeignKey(
                name: "FK_CONTRACTS_OFFER_VERSION",
                table: "CONTRACTS",
                column: "OFFER_VERSION_ID",
                principalTable: "OFFER_VERSIONS",
                principalColumn: "ID");

            migrationBuilder.AddForeignKey(
                name: "FK_LANDS_VERIFIED_CRITERION",
                table: "LANDS",
                column: "VERIFIED_AGAINST_CRITERION_ID",
                principalTable: "LAND_CRITERIA",
                principalColumn: "ID");

            migrationBuilder.AddForeignKey(
                name: "FK_OFFERS_ACCEPTED_VERSION",
                table: "OFFERS",
                column: "ACCEPTED_VERSION_ID",
                principalTable: "OFFER_VERSIONS",
                principalColumn: "ID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CONTRACTS_CANCELLATION_REQUESTER",
                table: "CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_CONTRACTS_OFFER_VERSION",
                table: "CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_LANDS_VERIFIED_CRITERION",
                table: "LANDS");

            migrationBuilder.DropForeignKey(
                name: "FK_OFFERS_ACCEPTED_VERSION",
                table: "OFFERS");

            migrationBuilder.DropTable(
                name: "TARIFF_BRACKETS");

            migrationBuilder.DropIndex(
                name: "IX_OFFERS_ACCEPTED_VERSION_ID",
                table: "OFFERS");

            migrationBuilder.DropIndex(
                name: "IX_LANDS_VERIFIED_AGAINST_CRITERION_ID",
                table: "LANDS");

            migrationBuilder.DropIndex(
                name: "IX_CONTRACTS_CANCELLATION_REQUESTED_BY_ID",
                table: "CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_CONTRACTS_OFFER_VERSION_ID",
                table: "CONTRACTS");

            migrationBuilder.DeleteData(
                table: "CONTRACT_STATUSES",
                keyColumn: "ID",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "CONTRACT_STATUSES",
                keyColumn: "ID",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "CONTRACT_STATUSES",
                keyColumn: "ID",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "CONTRACT_STATUSES",
                keyColumn: "ID",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "LAND_STATUSES",
                keyColumn: "ID",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "LAND_STATUSES",
                keyColumn: "ID",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "LAND_STATUSES",
                keyColumn: "ID",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "LAND_STATUSES",
                keyColumn: "ID",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "ROLES",
                keyColumn: "ID",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "ROLES",
                keyColumn: "ID",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "ROLES",
                keyColumn: "ID",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "USERS",
                keyColumn: "ID",
                keyValue: -1);

            migrationBuilder.DeleteData(
                table: "ROLES",
                keyColumn: "ID",
                keyValue: 4);

            migrationBuilder.DropColumn(
                name: "LOSS_PERCENTAGE",
                table: "REGIONS");

            migrationBuilder.DropColumn(
                name: "PEAK_SUN_HOURS_PER_DAY",
                table: "REGIONS");

            migrationBuilder.DropColumn(
                name: "WHEELING_FEE_PER_KWH",
                table: "REGIONS");

            migrationBuilder.DropColumn(
                name: "ACCEPTED_VERSION_ID",
                table: "OFFERS");

            migrationBuilder.DropColumn(
                name: "EFFECTIVE_COST_PER_KWH",
                table: "OFFER_VERSIONS");

            migrationBuilder.DropColumn(
                name: "EXPECTED_ANNUAL_REVENUE",
                table: "OFFER_VERSIONS");

            migrationBuilder.DropColumn(
                name: "PAYBACK_PERIOD_MONTHS",
                table: "OFFER_VERSIONS");

            migrationBuilder.DropColumn(
                name: "SOLAR_CELL_CAPACITY_KW",
                table: "OFFER_VERSIONS");

            migrationBuilder.DropColumn(
                name: "DOCUMENT_STORAGE_LOCATION",
                table: "LANDS");

            migrationBuilder.DropColumn(
                name: "VERIFIED_AGAINST_CRITERION_ID",
                table: "LANDS");

            migrationBuilder.DropColumn(
                name: "CANCELLATION_EFFECTIVE_DATE",
                table: "CONTRACTS");

            migrationBuilder.DropColumn(
                name: "CANCELLATION_REQUESTED_AT",
                table: "CONTRACTS");

            migrationBuilder.DropColumn(
                name: "CANCELLATION_REQUESTED_BY_ID",
                table: "CONTRACTS");

            migrationBuilder.DropColumn(
                name: "COMPENSATION_AMOUNT",
                table: "CONTRACTS");

            migrationBuilder.DropColumn(
                name: "DISPUTE_FLAGGED",
                table: "CONTRACTS");

            migrationBuilder.DropColumn(
                name: "INVESTOR_PENALTY_AMOUNT",
                table: "CONTRACTS");

            migrationBuilder.DropColumn(
                name: "NOTICE_PERIOD_DAYS",
                table: "CONTRACTS");

            migrationBuilder.DropColumn(
                name: "OFFER_VERSION_ID",
                table: "CONTRACTS");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ROLES",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
