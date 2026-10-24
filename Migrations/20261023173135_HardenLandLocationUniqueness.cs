using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NibrasWeb.Migrations
{
    /// <inheritdoc />
    public partial class HardenLandLocationUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_LAND_LOCATION",
                table: "LANDS");

            migrationBuilder.DropIndex(
                name: "UQ_LAND_LOCATION_GOV",
                table: "LANDS");

            migrationBuilder.CreateIndex(
                name: "UQ_LAND_LOCATION",
                table: "LANDS",
                columns: new[] { "REGION_ID", "VILLAGE", "BASIN", "PARCEL_NUMBER" },
                unique: true,
                filter: "[IS_DELETED] = 0 AND [REGION_ID] IS NOT NULL AND [VILLAGE] IS NOT NULL AND [BASIN] IS NOT NULL AND [PARCEL_NUMBER] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UQ_LAND_LOCATION_GOV",
                table: "LANDS",
                columns: new[] { "GOVERNORATE_ID", "VILLAGE", "BASIN", "PARCEL_NUMBER" },
                unique: true,
                filter: "[IS_DELETED] = 0 AND [GOVERNORATE_ID] IS NOT NULL AND [VILLAGE] IS NOT NULL AND [BASIN] IS NOT NULL AND [PARCEL_NUMBER] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback is restore-from-backup. Recreating the previous weaker filters can
            // fail on a database that already contains null-location legacy rows. This Down
            // intentionally fails loudly rather than hiding the problem.
            migrationBuilder.DropIndex(
                name: "UQ_LAND_LOCATION",
                table: "LANDS");

            migrationBuilder.DropIndex(
                name: "UQ_LAND_LOCATION_GOV",
                table: "LANDS");

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
        }
    }
}
