using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NIBRAS.API.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CONTRACT_STATUSES",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NAME = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CONTRACT__3214EC27CE14508F", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "DOCUMENT_TYPES",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NAME = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__DOCUMENT__3214EC27579B58DC", x => x.ID);
                });

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

            migrationBuilder.CreateTable(
                name: "OFFER_STATUSES",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NAME = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__OFFER_ST__3214EC27D14BDC1A", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "REGIONS",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NAME_AR = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NAME_EN = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__REGIONS__3214EC27BEC00DCF", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "ROLES",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NAME = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ROLES__3214EC276782D7D4", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "GRIDS",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    REGION_ID = table.Column<int>(type: "int", nullable: false),
                    NAME = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CAPACITY_MW = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    STATUS = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true, defaultValue: "Active")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__GRIDS__3214EC27C5B24747", x => x.ID);
                    table.ForeignKey(
                        name: "FK_GRIDS_REGIONS",
                        column: x => x.REGION_ID,
                        principalTable: "REGIONS",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "USERS",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ROLE_ID = table.Column<int>(type: "int", nullable: false),
                    FULL_NAME = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EMAIL = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    PHONE = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PASSWORD_HASH = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    IS_DELETED = table.Column<bool>(type: "bit", nullable: false),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getdate())"),
                    DatefoBirth = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__USERS__3214EC27D0F84BA1", x => x.ID);
                    table.ForeignKey(
                        name: "FK_USERS_ROLES",
                        column: x => x.ROLE_ID,
                        principalTable: "ROLES",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "LAND_CRITERIA",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MIN_AREA_DONUM = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    MAX_SLOPE_PCT = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    MAX_GRID_DISTANCE_KM = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    MIN_SOLAR_IRRADIANCE = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    MIN_ELEVATION_M = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    UPDATED_BY_ID = table.Column<int>(type: "int", nullable: false),
                    UPDATED_AT = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__LAND_CRI__3214EC276031D9CC", x => x.ID);
                    table.ForeignKey(
                        name: "FK_LC_USERS",
                        column: x => x.UPDATED_BY_ID,
                        principalTable: "USERS",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "LANDS",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LANDLORD_ID = table.Column<int>(type: "int", nullable: false),
                    REGION_ID = table.Column<int>(type: "int", nullable: false),
                    LAND_NUMBER = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AREA_DONUM = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    SLOPE_PERCENTAGE = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    DISTANCE_TO_GRID_KM = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    SOLAR_IRRADIANCE = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    ELEVATION_M = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    IS_DELETED = table.Column<bool>(type: "bit", nullable: false),
                    LAND_STATUS_ID = table.Column<int>(type: "int", nullable: false),
                    DATA_VERIFIED_BY_ADMIN = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__LANDS__3214EC278FA5F43E", x => x.ID);
                    table.ForeignKey(
                        name: "FK_LANDS_LANDLORDS",
                        column: x => x.LANDLORD_ID,
                        principalTable: "USERS",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_LANDS_REGIONS",
                        column: x => x.REGION_ID,
                        principalTable: "REGIONS",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_LANDS_STATUS",
                        column: x => x.LAND_STATUS_ID,
                        principalTable: "LAND_STATUSES",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "DELETION_REQUESTS",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LAND_ID = table.Column<int>(type: "int", nullable: false),
                    REQUESTED_BY_ID = table.Column<int>(type: "int", nullable: false),
                    REASON = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    STATUS = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true, defaultValue: "Pending"),
                    ADMIN_COMMENT = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__DELETION__3214EC278A03ACC4", x => x.ID);
                    table.ForeignKey(
                        name: "FK_DELETION_LANDS",
                        column: x => x.LAND_ID,
                        principalTable: "LANDS",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_DELETION_USERS",
                        column: x => x.REQUESTED_BY_ID,
                        principalTable: "USERS",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "LAND_DOCUMENTS",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LAND_ID = table.Column<int>(type: "int", nullable: false),
                    DOCUMENT_TYPE_ID = table.Column<int>(type: "int", nullable: false),
                    FILE_PATH = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    VERSION = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    STATUS = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true, defaultValue: "Pending"),
                    UPLOADED_AT = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__LAND_DOC__3214EC27A38FC9A9", x => x.ID);
                    table.ForeignKey(
                        name: "FK_LD_DOCTYPES",
                        column: x => x.DOCUMENT_TYPE_ID,
                        principalTable: "DOCUMENT_TYPES",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_LD_LANDS",
                        column: x => x.LAND_ID,
                        principalTable: "LANDS",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "LAND_STATUS_HISTORY",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LAND_ID = table.Column<int>(type: "int", nullable: false),
                    STATUS_ID = table.Column<int>(type: "int", nullable: false),
                    CHANGED_BY_ID = table.Column<int>(type: "int", nullable: false),
                    REASON = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CHANGED_AT = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__LAND_STA__3214EC27F466086B", x => x.ID);
                    table.ForeignKey(
                        name: "FK_LSH_LANDS",
                        column: x => x.LAND_ID,
                        principalTable: "LANDS",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_LSH_USERS",
                        column: x => x.CHANGED_BY_ID,
                        principalTable: "USERS",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "OFFERS",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LAND_ID = table.Column<int>(type: "int", nullable: false),
                    INVESTOR_ID = table.Column<int>(type: "int", nullable: false),
                    REQUIRED_CAPACITY_MW = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    STATUS_ID = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getdate())"),
                    IS_DELETED = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__OFFERS__3214EC277FE0E225", x => x.ID);
                    table.ForeignKey(
                        name: "FK_OFFERS_INVESTORS",
                        column: x => x.INVESTOR_ID,
                        principalTable: "USERS",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_OFFERS_LANDS",
                        column: x => x.LAND_ID,
                        principalTable: "LANDS",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_OFFERS_STATUS",
                        column: x => x.STATUS_ID,
                        principalTable: "OFFER_STATUSES",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "CONTRACTS",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OFFER_ID = table.Column<int>(type: "int", nullable: false),
                    LAND_ID = table.Column<int>(type: "int", nullable: false),
                    INVESTOR_ID = table.Column<int>(type: "int", nullable: false),
                    LANDLORD_ID = table.Column<int>(type: "int", nullable: false),
                    STATUS_ID = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    INVESTOR_SIGNED_AT = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ADMIN_REVIEWED_AT = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LANDLORD_SIGNED_AT = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ADMIN_SIGNED_AT = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CONTRACT__3214EC2738767384", x => x.ID);
                    table.ForeignKey(
                        name: "FK_CONTRACTS_INVESTORS",
                        column: x => x.INVESTOR_ID,
                        principalTable: "USERS",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_CONTRACTS_LANDLORDS",
                        column: x => x.LANDLORD_ID,
                        principalTable: "USERS",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_CONTRACTS_LANDS",
                        column: x => x.LAND_ID,
                        principalTable: "LANDS",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_CONTRACTS_OFFERS",
                        column: x => x.OFFER_ID,
                        principalTable: "OFFERS",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_CONTRACTS_STATUS",
                        column: x => x.STATUS_ID,
                        principalTable: "CONTRACT_STATUSES",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "OFFER_VERSIONS",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OFFER_ID = table.Column<int>(type: "int", nullable: false),
                    VERSION_NUMBER = table.Column<int>(type: "int", nullable: false),
                    LANDLORD_SHARE_PCT = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    DURATION_YEARS = table.Column<int>(type: "int", nullable: true),
                    START_DATE = table.Column<DateOnly>(type: "date", nullable: true),
                    INSTALLATION_COST = table.Column<decimal>(type: "decimal(12,2)", nullable: true),
                    CREATED_BY_ID = table.Column<int>(type: "int", nullable: false),
                    REJECTION_REASON = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__OFFER_VE__3214EC27145811E1", x => x.ID);
                    table.ForeignKey(
                        name: "FK_OV_OFFERS",
                        column: x => x.OFFER_ID,
                        principalTable: "OFFERS",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_OV_USERS",
                        column: x => x.CREATED_BY_ID,
                        principalTable: "USERS",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "CONTRACT_REVIEWS",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CONTRACT_ID = table.Column<int>(type: "int", nullable: false),
                    REVIEWER_ID = table.Column<int>(type: "int", nullable: false),
                    DECISION = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    REASON = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CONTRACT__3214EC273AB66E59", x => x.ID);
                    table.ForeignKey(
                        name: "FK_CR_CONTRACTS",
                        column: x => x.CONTRACT_ID,
                        principalTable: "CONTRACTS",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_CR_USERS",
                        column: x => x.REVIEWER_ID,
                        principalTable: "USERS",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "GRID_CAPACITY_RESERVATIONS",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GRID_ID = table.Column<int>(type: "int", nullable: false),
                    CONTRACT_ID = table.Column<int>(type: "int", nullable: false),
                    RESERVED_MW = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__GRID_CAP__3214EC27C682FA92", x => x.ID);
                    table.ForeignKey(
                        name: "FK_GCR_CONTRACTS",
                        column: x => x.CONTRACT_ID,
                        principalTable: "CONTRACTS",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_GCR_GRIDS",
                        column: x => x.GRID_ID,
                        principalTable: "GRIDS",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_REVIEWS_CONTRACT_ID",
                table: "CONTRACT_REVIEWS",
                column: "CONTRACT_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_REVIEWS_REVIEWER_ID",
                table: "CONTRACT_REVIEWS",
                column: "REVIEWER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACTS_INVESTOR_ID",
                table: "CONTRACTS",
                column: "INVESTOR_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACTS_LAND_ID",
                table: "CONTRACTS",
                column: "LAND_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACTS_LANDLORD_ID",
                table: "CONTRACTS",
                column: "LANDLORD_ID");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACTS_STATUS_ID",
                table: "CONTRACTS",
                column: "STATUS_ID");

            migrationBuilder.CreateIndex(
                name: "UQ__CONTRACT__96E0508AA6D26B86",
                table: "CONTRACTS",
                column: "OFFER_ID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DELETION_REQUESTS_LAND_ID",
                table: "DELETION_REQUESTS",
                column: "LAND_ID");

            migrationBuilder.CreateIndex(
                name: "IX_DELETION_REQUESTS_REQUESTED_BY_ID",
                table: "DELETION_REQUESTS",
                column: "REQUESTED_BY_ID");

            migrationBuilder.CreateIndex(
                name: "IX_GRID_CAPACITY_RESERVATIONS_GRID_ID",
                table: "GRID_CAPACITY_RESERVATIONS",
                column: "GRID_ID");

            migrationBuilder.CreateIndex(
                name: "UQ__GRID_CAP__3F5DFF15DEC96C5D",
                table: "GRID_CAPACITY_RESERVATIONS",
                column: "CONTRACT_ID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GRIDS_REGION_ID",
                table: "GRIDS",
                column: "REGION_ID");

            migrationBuilder.CreateIndex(
                name: "IX_LAND_CRITERIA_UPDATED_BY_ID",
                table: "LAND_CRITERIA",
                column: "UPDATED_BY_ID");

            migrationBuilder.CreateIndex(
                name: "IX_LAND_DOCUMENTS_DOCUMENT_TYPE_ID",
                table: "LAND_DOCUMENTS",
                column: "DOCUMENT_TYPE_ID");

            migrationBuilder.CreateIndex(
                name: "IX_LAND_DOCUMENTS_LAND_ID",
                table: "LAND_DOCUMENTS",
                column: "LAND_ID");

            migrationBuilder.CreateIndex(
                name: "IX_LAND_STATUS_HISTORY_CHANGED_BY_ID",
                table: "LAND_STATUS_HISTORY",
                column: "CHANGED_BY_ID");

            migrationBuilder.CreateIndex(
                name: "IX_LAND_STATUS_HISTORY_LAND_ID",
                table: "LAND_STATUS_HISTORY",
                column: "LAND_ID");

            migrationBuilder.CreateIndex(
                name: "IX_LANDS_LAND_STATUS_ID",
                table: "LANDS",
                column: "LAND_STATUS_ID");

            migrationBuilder.CreateIndex(
                name: "IX_LANDS_LANDLORD_ID",
                table: "LANDS",
                column: "LANDLORD_ID");

            migrationBuilder.CreateIndex(
                name: "IX_LANDS_REGION_ID",
                table: "LANDS",
                column: "REGION_ID");

            migrationBuilder.CreateIndex(
                name: "UQ_LAND_NUMBER_REGION",
                table: "LANDS",
                columns: new[] { "LAND_NUMBER", "REGION_ID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OFFER_VERSIONS_CREATED_BY_ID",
                table: "OFFER_VERSIONS",
                column: "CREATED_BY_ID");

            migrationBuilder.CreateIndex(
                name: "IX_OFFER_VERSIONS_OFFER_ID",
                table: "OFFER_VERSIONS",
                column: "OFFER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_OFFERS_INVESTOR_ID",
                table: "OFFERS",
                column: "INVESTOR_ID");

            migrationBuilder.CreateIndex(
                name: "IX_OFFERS_LAND_ID",
                table: "OFFERS",
                column: "LAND_ID");

            migrationBuilder.CreateIndex(
                name: "IX_OFFERS_STATUS_ID",
                table: "OFFERS",
                column: "STATUS_ID");

            migrationBuilder.CreateIndex(
                name: "IX_USERS_ROLE_ID",
                table: "USERS",
                column: "ROLE_ID");

            migrationBuilder.CreateIndex(
                name: "UQ__USERS__161CF724F5C148C0",
                table: "USERS",
                column: "EMAIL",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ__USERS__D4FA0A2658412415",
                table: "USERS",
                column: "PHONE",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CONTRACT_REVIEWS");

            migrationBuilder.DropTable(
                name: "DELETION_REQUESTS");

            migrationBuilder.DropTable(
                name: "GRID_CAPACITY_RESERVATIONS");

            migrationBuilder.DropTable(
                name: "LAND_CRITERIA");

            migrationBuilder.DropTable(
                name: "LAND_DOCUMENTS");

            migrationBuilder.DropTable(
                name: "LAND_STATUS_HISTORY");

            migrationBuilder.DropTable(
                name: "OFFER_VERSIONS");

            migrationBuilder.DropTable(
                name: "CONTRACTS");

            migrationBuilder.DropTable(
                name: "GRIDS");

            migrationBuilder.DropTable(
                name: "DOCUMENT_TYPES");

            migrationBuilder.DropTable(
                name: "OFFERS");

            migrationBuilder.DropTable(
                name: "CONTRACT_STATUSES");

            migrationBuilder.DropTable(
                name: "LANDS");

            migrationBuilder.DropTable(
                name: "OFFER_STATUSES");

            migrationBuilder.DropTable(
                name: "USERS");

            migrationBuilder.DropTable(
                name: "REGIONS");

            migrationBuilder.DropTable(
                name: "LAND_STATUSES");

            migrationBuilder.DropTable(
                name: "ROLES");
        }
    }
}
