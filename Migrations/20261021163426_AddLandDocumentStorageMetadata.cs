using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NibrasWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddLandDocumentStorageMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CONTENT_TYPE",
                table: "LAND_DOCUMENTS",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FILE_SIZE_BYTES",
                table: "LAND_DOCUMENTS",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ORIGINAL_FILE_NAME",
                table: "LAND_DOCUMENTS",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SHA256",
                table: "LAND_DOCUMENTS",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CONTENT_TYPE",
                table: "LAND_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "FILE_SIZE_BYTES",
                table: "LAND_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "ORIGINAL_FILE_NAME",
                table: "LAND_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "SHA256",
                table: "LAND_DOCUMENTS");
        }
    }
}
