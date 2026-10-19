using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NibrasWeb.Migrations
{
    /// <summary>
    /// Landlord correction and change-request flow.
    ///
    /// Adds the <c>IS_HIDDEN_FROM_INVESTORS</c> flag used to hide an approved land for
    /// the whole duration of a change-request review, and the append-style
    /// <c>LAND_CHANGE_REQUESTS</c> table that preserves the immutable old/new field
    /// snapshots, the reason, the supporting documents, the requester/reviewer
    /// identities, the timestamps and the final decision.
    ///
    /// No reference-status row is required: <c>LAND_STATUS_ID</c> is a plain int column
    /// (the <c>LAND_STATUSES</c> table and its FK were removed in
    /// <c>Step1_FixBreakages</c>), so the new <c>PendingSuperAdminReview</c> status is a
    /// pure code change and needs no schema change here.
    /// </summary>
    public partial class Phase2_LandCorrectionAndChangeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IS_HIDDEN_FROM_INVESTORS",
                table: "LANDS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "LAND_CHANGE_REQUESTS",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LAND_ID = table.Column<int>(type: "int", nullable: false),
                    REQUESTED_BY_ID = table.Column<int>(type: "int", nullable: false),
                    REASON = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    STATUS = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    REQUESTED_AT = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getdate())"),
                    REVIEWED_BY_ID = table.Column<int>(type: "int", nullable: true),
                    REVIEWED_AT = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DECISION_REASON = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OLD_SNAPSHOT = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NEW_SNAPSHOT = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SUPPORTING_DOCUMENTS = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ROW_VERSION = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LAND_CHANGE_REQUESTS", x => x.ID);
                    table.ForeignKey(
                        name: "FK_LCR_LANDS",
                        column: x => x.LAND_ID,
                        principalTable: "LANDS",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_LCR_REQUESTER",
                        column: x => x.REQUESTED_BY_ID,
                        principalTable: "USERS",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_LCR_REVIEWER",
                        column: x => x.REVIEWED_BY_ID,
                        principalTable: "USERS",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LAND_CHANGE_REQUESTS_LAND_ID",
                table: "LAND_CHANGE_REQUESTS",
                column: "LAND_ID");

            migrationBuilder.CreateIndex(
                name: "IX_LAND_CHANGE_REQUESTS_REQUESTED_BY_ID",
                table: "LAND_CHANGE_REQUESTS",
                column: "REQUESTED_BY_ID");

            migrationBuilder.CreateIndex(
                name: "IX_LAND_CHANGE_REQUESTS_REVIEWED_BY_ID",
                table: "LAND_CHANGE_REQUESTS",
                column: "REVIEWED_BY_ID");

            migrationBuilder.CreateIndex(
                name: "IX_LAND_CHANGE_REQUESTS_STATUS",
                table: "LAND_CHANGE_REQUESTS",
                column: "STATUS");

            // At most one change request may be awaiting a decision per land. The filter
            // matches the stored string status ("Pending") so a decided request never blocks
            // a later one, while a second concurrent pending request fails at the database.
            migrationBuilder.CreateIndex(
                name: "UX_LAND_CHANGE_REQUESTS_PENDING_LAND",
                table: "LAND_CHANGE_REQUESTS",
                column: "LAND_ID",
                unique: true,
                filter: "[STATUS] = N'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LAND_CHANGE_REQUESTS");

            migrationBuilder.DropColumn(
                name: "IS_HIDDEN_FROM_INVESTORS",
                table: "LANDS");
        }
    }
}
