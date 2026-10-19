using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NibrasWeb.Migrations
{
    /// <inheritdoc />
    public partial class DropOfferStatusTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CONTRACTS_STATUS",
                table: "CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_OFFERS_STATUS",
                table: "OFFERS");

            migrationBuilder.DropTable(
                name: "CONTRACT_STATUSES");

            migrationBuilder.DropTable(
                name: "OFFER_STATUSES");

            migrationBuilder.DropIndex(
                name: "IX_OFFERS_STATUS_ID",
                table: "OFFERS");

            migrationBuilder.DropIndex(
                name: "IX_CONTRACTS_STATUS_ID",
                table: "CONTRACTS");

            migrationBuilder.DropColumn(
                name: "IS_DELETED",
                table: "OFFERS");

            migrationBuilder.AlterColumn<int>(
                name: "STATUS_ID",
                table: "OFFERS",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentAmount",
                table: "OFFERS",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "LastProposedById",
                table: "OFFERS",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "apdateAt",
                table: "OFFERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LandStatusId",
                table: "LAND_STATUS_HISTORY",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("UPDATE [LAND_STATUS_HISTORY] SET [LandStatusId] = [STATUS_ID] WHERE [LandStatusId] = 0");

            migrationBuilder.AlterColumn<int>(
                name: "STATUS_ID",
                table: "CONTRACTS",
                type: "int",
                nullable: false,
                defaultValue: 2,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_LAND_STATUS_HISTORY_LandStatusId",
                table: "LAND_STATUS_HISTORY",
                column: "LandStatusId");

            migrationBuilder.AddForeignKey(
                name: "FK_LAND_STATUS_HISTORY_LAND_STATUSES_LandStatusId",
                table: "LAND_STATUS_HISTORY",
                column: "LandStatusId",
                principalTable: "LAND_STATUSES",
                principalColumn: "ID",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LAND_STATUS_HISTORY_LAND_STATUSES_LandStatusId",
                table: "LAND_STATUS_HISTORY");

            migrationBuilder.DropIndex(
                name: "IX_LAND_STATUS_HISTORY_LandStatusId",
                table: "LAND_STATUS_HISTORY");

            migrationBuilder.DropColumn(
                name: "CurrentAmount",
                table: "OFFERS");

            migrationBuilder.DropColumn(
                name: "LastProposedById",
                table: "OFFERS");

            migrationBuilder.DropColumn(
                name: "apdateAt",
                table: "OFFERS");

            migrationBuilder.DropColumn(
                name: "LandStatusId",
                table: "LAND_STATUS_HISTORY");

            migrationBuilder.AlterColumn<int>(
                name: "STATUS_ID",
                table: "OFFERS",
                type: "int",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<bool>(
                name: "IS_DELETED",
                table: "OFFERS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "STATUS_ID",
                table: "CONTRACTS",
                type: "int",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 2);

            migrationBuilder.CreateTable(
                name: "CONTRACT_STATUSES",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NAME = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CONTRACT__3214EC27CE14508F", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "OFFER_STATUSES",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NAME = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__OFFER_ST__3214EC27D14BDC1A", x => x.ID);
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

            migrationBuilder.CreateIndex(
                name: "IX_OFFERS_STATUS_ID",
                table: "OFFERS",
                column: "STATUS_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACTS_STATUS_ID",
                table: "CONTRACTS",
                column: "STATUS_ID");

            migrationBuilder.AddForeignKey(
                name: "FK_CONTRACTS_STATUS",
                table: "CONTRACTS",
                column: "STATUS_ID",
                principalTable: "CONTRACT_STATUSES",
                principalColumn: "ID");

            migrationBuilder.AddForeignKey(
                name: "FK_OFFERS_STATUS",
                table: "OFFERS",
                column: "STATUS_ID",
                principalTable: "OFFER_STATUSES",
                principalColumn: "ID");
        }
    }
}
