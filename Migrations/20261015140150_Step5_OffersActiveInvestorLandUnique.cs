using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NibrasWeb.Migrations
{
    /// <inheritdoc />
    public partial class Step5_OffersActiveInvestorLandUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_OFFERS_ACTIVE_INVESTOR_LAND",
                table: "OFFERS",
                columns: new[] { "LAND_ID", "INVESTOR_ID" },
                unique: true,
                filter: "[STATUS_ID] IN (1, 2, 3, 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_OFFERS_ACTIVE_INVESTOR_LAND",
                table: "OFFERS");
        }
    }
}
