using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NibrasWeb.Migrations
{
    /// <inheritdoc />
    public partial class Step1_FixBreakages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LAND_STATUS_HISTORY_LAND_STATUSES_LandStatusId",
                table: "LAND_STATUS_HISTORY");

            migrationBuilder.DropForeignKey(
                name: "FK_LANDS_STATUS",
                table: "LANDS");

            migrationBuilder.DropTable(
                name: "LAND_STATUSES");

            migrationBuilder.DropIndex(
                name: "IX_OFFER_VERSIONS_OFFER_ID",
                table: "OFFER_VERSIONS");

            migrationBuilder.DropIndex(
                name: "IX_LANDS_LAND_STATUS_ID",
                table: "LANDS");

            migrationBuilder.DropIndex(
                name: "IX_LAND_STATUS_HISTORY_LandStatusId",
                table: "LAND_STATUS_HISTORY");

            migrationBuilder.DropColumn(
                name: "LandStatusId",
                table: "LAND_STATUS_HISTORY");

            migrationBuilder.AlterColumn<int>(
                name: "STATUS_ID",
                table: "CONTRACTS",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 2);

            migrationBuilder.CreateIndex(
                name: "UX_OFFER_VERSIONS_CURRENT_OFFER",
                table: "OFFER_VERSIONS",
                column: "OFFER_ID",
                unique: true,
                filter: "[IS_CURRENT] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_OFFER_VERSIONS_CURRENT_OFFER",
                table: "OFFER_VERSIONS");

            migrationBuilder.AddColumn<int>(
                name: "LandStatusId",
                table: "LAND_STATUS_HISTORY",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "STATUS_ID",
                table: "CONTRACTS",
                type: "int",
                nullable: false,
                defaultValue: 2,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateTable(
                name: "LAND_STATUSES",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NAME = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__LAND_STA__3214EC27D4FB7967", x => x.ID);
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

            migrationBuilder.CreateIndex(
                name: "IX_OFFER_VERSIONS_OFFER_ID",
                table: "OFFER_VERSIONS",
                column: "OFFER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_LANDS_LAND_STATUS_ID",
                table: "LANDS",
                column: "LAND_STATUS_ID");

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

            migrationBuilder.AddForeignKey(
                name: "FK_LANDS_STATUS",
                table: "LANDS",
                column: "LAND_STATUS_ID",
                principalTable: "LAND_STATUSES",
                principalColumn: "ID");
        }
    }
}
