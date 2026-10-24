using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NibrasWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddContractVersionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CONTRACT_VERSIONS",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CONTRACT_ID = table.Column<int>(type: "int", nullable: false),
                    VERSION_NUMBER = table.Column<int>(type: "int", nullable: false),
                    VERSION_KIND = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EVENT_TYPE = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    STATUS = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PREVIOUS_VERSION_ID = table.Column<int>(type: "int", nullable: true),
                    TARGET_VERSION_ID = table.Column<int>(type: "int", nullable: true),
                    OFFER_ID = table.Column<int>(type: "int", nullable: false),
                    OFFER_VERSION_ID = table.Column<int>(type: "int", nullable: true),
                    TERMS_SNAPSHOT = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    REASON = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CREATED_BY_ID = table.Column<int>(type: "int", nullable: true),
                    CREATED_BY_NAME_SNAPSHOT = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CREATED_BY_ROLE_SNAPSHOT = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CONTRACT_VERSIONS", x => x.ID);
                    table.ForeignKey(
                        name: "FK_CV_CONTRACTS",
                        column: x => x.CONTRACT_ID,
                        principalTable: "CONTRACTS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CV_OFFERS",
                        column: x => x.OFFER_ID,
                        principalTable: "OFFERS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CV_OFFER_VERSION",
                        column: x => x.OFFER_VERSION_ID,
                        principalTable: "OFFER_VERSIONS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CV_PREVIOUS",
                        column: x => x.PREVIOUS_VERSION_ID,
                        principalTable: "CONTRACT_VERSIONS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CV_TARGET",
                        column: x => x.TARGET_VERSION_ID,
                        principalTable: "CONTRACT_VERSIONS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_VERSIONS_OFFER_ID",
                table: "CONTRACT_VERSIONS",
                column: "OFFER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_VERSIONS_OFFER_VERSION_ID",
                table: "CONTRACT_VERSIONS",
                column: "OFFER_VERSION_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_VERSIONS_PREVIOUS_VERSION_ID",
                table: "CONTRACT_VERSIONS",
                column: "PREVIOUS_VERSION_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_VERSIONS_TARGET_VERSION_ID",
                table: "CONTRACT_VERSIONS",
                column: "TARGET_VERSION_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CV_CONTRACT_CREATED",
                table: "CONTRACT_VERSIONS",
                columns: new[] { "CONTRACT_ID", "CREATED_AT" });

            migrationBuilder.CreateIndex(
                name: "UX_CV_DRAFT_VERSION",
                table: "CONTRACT_VERSIONS",
                columns: new[] { "CONTRACT_ID", "VERSION_NUMBER" },
                unique: true,
                filter: "[VERSION_KIND] = 'Draft'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CONTRACT_VERSIONS");
        }
    }
}
