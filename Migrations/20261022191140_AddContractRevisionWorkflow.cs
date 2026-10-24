using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NibrasWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddContractRevisionWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CONTRACT_REVISIONS",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CONTRACT_ID = table.Column<int>(type: "int", nullable: false),
                    BASE_DRAFT_VERSION_ID = table.Column<int>(type: "int", nullable: false),
                    STATUS = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    REASON = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PROPOSED_TERMS_SNAPSHOT = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PROPOSED_BY_ID = table.Column<int>(type: "int", nullable: false),
                    PROPOSED_BY_NAME_SNAPSHOT = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PROPOSED_BY_ROLE_SNAPSHOT = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PROPOSED_AT = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ADMIN_REVIEWED_BY_ID = table.Column<int>(type: "int", nullable: true),
                    ADMIN_REVIEWED_BY_NAME_SNAPSHOT = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ADMIN_REVIEWED_BY_ROLE_SNAPSHOT = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ADMIN_REVIEWED_AT = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ADMIN_DECISION = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ESCALATION_REASON = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SUPERADMIN_DECIDED_BY_ID = table.Column<int>(type: "int", nullable: true),
                    SUPERADMIN_DECIDED_BY_NAME_SNAPSHOT = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SUPERADMIN_DECIDED_BY_ROLE_SNAPSHOT = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SUPERADMIN_DECIDED_AT = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SUPERADMIN_DECISION = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    SUPERADMIN_REASON = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    NEW_OFFER_VERSION_ID = table.Column<int>(type: "int", nullable: true),
                    NEW_DRAFT_VERSION_ID = table.Column<int>(type: "int", nullable: true),
                    CLOSED_AT = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CONTRACT_REVISIONS", x => x.ID);
                    table.ForeignKey(
                        name: "FK_CONTRACT_REV_ADMIN_REVIEWED_BY",
                        column: x => x.ADMIN_REVIEWED_BY_ID,
                        principalTable: "USERS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CONTRACT_REV_BASE_DRAFT",
                        column: x => x.BASE_DRAFT_VERSION_ID,
                        principalTable: "CONTRACT_VERSIONS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CONTRACT_REV_CONTRACTS",
                        column: x => x.CONTRACT_ID,
                        principalTable: "CONTRACTS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CONTRACT_REV_NEW_DRAFT",
                        column: x => x.NEW_DRAFT_VERSION_ID,
                        principalTable: "CONTRACT_VERSIONS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CONTRACT_REV_NEW_OFFER_VERSION",
                        column: x => x.NEW_OFFER_VERSION_ID,
                        principalTable: "OFFER_VERSIONS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CONTRACT_REV_PROPOSED_BY",
                        column: x => x.PROPOSED_BY_ID,
                        principalTable: "USERS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CONTRACT_REV_SUPERADMIN_DECIDED_BY",
                        column: x => x.SUPERADMIN_DECIDED_BY_ID,
                        principalTable: "USERS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CONTRACT_REVISION_CONSENTS",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    REVISION_ID = table.Column<int>(type: "int", nullable: false),
                    PARTY_USER_ID = table.Column<int>(type: "int", nullable: false),
                    PARTY_NAME_SNAPSHOT = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PARTY_ROLE_SNAPSHOT = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DECISION = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    REASON = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CONTRACT_REVISION_CONSENTS", x => x.ID);
                    table.ForeignKey(
                        name: "FK_CONTRACT_REV_CONSENT_PARTY",
                        column: x => x.PARTY_USER_ID,
                        principalTable: "USERS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CONTRACT_REV_CONSENT_REVISION",
                        column: x => x.REVISION_ID,
                        principalTable: "CONTRACT_REVISIONS",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_REVISION_CONSENTS_PARTY_USER_ID",
                table: "CONTRACT_REVISION_CONSENTS",
                column: "PARTY_USER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CRC_REVISION",
                table: "CONTRACT_REVISION_CONSENTS",
                column: "REVISION_ID");

            migrationBuilder.CreateIndex(
                name: "UX_CRC_REVISION_PARTY",
                table: "CONTRACT_REVISION_CONSENTS",
                columns: new[] { "REVISION_ID", "PARTY_USER_ID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_REVISIONS_ADMIN_REVIEWED_BY_ID",
                table: "CONTRACT_REVISIONS",
                column: "ADMIN_REVIEWED_BY_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_REVISIONS_BASE_DRAFT_VERSION_ID",
                table: "CONTRACT_REVISIONS",
                column: "BASE_DRAFT_VERSION_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_REVISIONS_NEW_DRAFT_VERSION_ID",
                table: "CONTRACT_REVISIONS",
                column: "NEW_DRAFT_VERSION_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_REVISIONS_NEW_OFFER_VERSION_ID",
                table: "CONTRACT_REVISIONS",
                column: "NEW_OFFER_VERSION_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_REVISIONS_PROPOSED_BY_ID",
                table: "CONTRACT_REVISIONS",
                column: "PROPOSED_BY_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_REVISIONS_SUPERADMIN_DECIDED_BY_ID",
                table: "CONTRACT_REVISIONS",
                column: "SUPERADMIN_DECIDED_BY_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CR_CONTRACT_PROPOSED",
                table: "CONTRACT_REVISIONS",
                columns: new[] { "CONTRACT_ID", "PROPOSED_AT" });

            migrationBuilder.CreateIndex(
                name: "UX_CR_OPEN_CONTRACT",
                table: "CONTRACT_REVISIONS",
                column: "CONTRACT_ID",
                unique: true,
                filter: "[CLOSED_AT] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CONTRACT_REVISION_CONSENTS");

            migrationBuilder.DropTable(
                name: "CONTRACT_REVISIONS");
        }
    }
}
