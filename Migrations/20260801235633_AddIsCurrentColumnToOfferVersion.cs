using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NibrasWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddIsCurrentColumnToOfferVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IS_CURRENT",
                table: "OFFER_VERSIONS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE ov
                SET ov.IS_CURRENT = 1
                FROM OFFER_VERSIONS ov
                INNER JOIN (
                    SELECT OFFER_ID, MAX(VERSION_NUMBER) AS MaxVersion
                    FROM OFFER_VERSIONS
                    GROUP BY OFFER_ID
                ) latest ON ov.OFFER_ID = latest.OFFER_ID AND ov.VERSION_NUMBER = latest.MaxVersion
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IS_CURRENT",
                table: "OFFER_VERSIONS");
        }
    }
}
