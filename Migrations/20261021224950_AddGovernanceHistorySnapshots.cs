using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NibrasWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddGovernanceHistorySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "APPROVED_BY_NAME_SNAPSHOT",
                table: "USERS",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "APPROVED_BY_ROLE_SNAPSHOT",
                table: "USERS",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ACTOR_NAME_SNAPSHOT",
                table: "USER_SECURITY_EVENTS",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ACTOR_ROLE_SNAPSHOT",
                table: "USER_SECURITY_EVENTS",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PREVIOUS_CRITERION_ID",
                table: "LAND_CRITERIA",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UPDATED_BY_NAME_SNAPSHOT",
                table: "LAND_CRITERIA",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UPDATED_BY_ROLE_SNAPSHOT",
                table: "LAND_CRITERIA",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AREA_DONUM_SNAPSHOT",
                table: "LAND_ASSESSMENTS",
                type: "decimal(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CRITERION_ID",
                table: "LAND_ASSESSMENTS",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LAND_CRITERIA_PREVIOUS_CRITERION_ID",
                table: "LAND_CRITERIA",
                column: "PREVIOUS_CRITERION_ID");

            migrationBuilder.CreateIndex(
                name: "IX_LAND_ASSESSMENTS_CRITERION_ID",
                table: "LAND_ASSESSMENTS",
                column: "CRITERION_ID");

            migrationBuilder.AddForeignKey(
                name: "FK_LA_CRITERIA",
                table: "LAND_ASSESSMENTS",
                column: "CRITERION_ID",
                principalTable: "LAND_CRITERIA",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LC_PREVIOUS",
                table: "LAND_CRITERIA",
                column: "PREVIOUS_CRITERION_ID",
                principalTable: "LAND_CRITERIA",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LA_CRITERIA",
                table: "LAND_ASSESSMENTS");

            migrationBuilder.DropForeignKey(
                name: "FK_LC_PREVIOUS",
                table: "LAND_CRITERIA");

            migrationBuilder.DropIndex(
                name: "IX_LAND_CRITERIA_PREVIOUS_CRITERION_ID",
                table: "LAND_CRITERIA");

            migrationBuilder.DropIndex(
                name: "IX_LAND_ASSESSMENTS_CRITERION_ID",
                table: "LAND_ASSESSMENTS");

            migrationBuilder.DropColumn(
                name: "APPROVED_BY_NAME_SNAPSHOT",
                table: "USERS");

            migrationBuilder.DropColumn(
                name: "APPROVED_BY_ROLE_SNAPSHOT",
                table: "USERS");

            migrationBuilder.DropColumn(
                name: "ACTOR_NAME_SNAPSHOT",
                table: "USER_SECURITY_EVENTS");

            migrationBuilder.DropColumn(
                name: "ACTOR_ROLE_SNAPSHOT",
                table: "USER_SECURITY_EVENTS");

            migrationBuilder.DropColumn(
                name: "PREVIOUS_CRITERION_ID",
                table: "LAND_CRITERIA");

            migrationBuilder.DropColumn(
                name: "UPDATED_BY_NAME_SNAPSHOT",
                table: "LAND_CRITERIA");

            migrationBuilder.DropColumn(
                name: "UPDATED_BY_ROLE_SNAPSHOT",
                table: "LAND_CRITERIA");

            migrationBuilder.DropColumn(
                name: "AREA_DONUM_SNAPSHOT",
                table: "LAND_ASSESSMENTS");

            migrationBuilder.DropColumn(
                name: "CRITERION_ID",
                table: "LAND_ASSESSMENTS");
        }
    }
}
