using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NibrasWeb.Migrations
{
    /// <inheritdoc />
    public partial class EmailVerificationUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_USERS_PENDING_EMAIL",
                table: "USERS",
                column: "PENDING_EMAIL",
                unique: true,
                filter: "[PENDING_EMAIL] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_EMAIL_VERIFICATION_TOKENS_LIVE",
                table: "EMAIL_VERIFICATION_TOKENS",
                columns: new[] { "USER_ID", "PURPOSE" },
                unique: true,
                filter: "[CONSUMED_AT] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_USERS_PENDING_EMAIL",
                table: "USERS");

            migrationBuilder.DropIndex(
                name: "UX_EMAIL_VERIFICATION_TOKENS_LIVE",
                table: "EMAIL_VERIFICATION_TOKENS");
        }
    }
}
