using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NibrasWeb.Migrations
{
    /// <inheritdoc />
    public partial class EmailVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Email-ownership state. The three USERS columns are nullable and carry NO default:
            // NULL means "pre-existing account, verified under the rules in force before the
            // verification feature existed". That keeps every existing account working and
            // fabricates no value for it - no backfill UPDATE, no DEFAULT 0 - so the only rows
            // that are unverified are the ones a verification flow explicitly created.
            // PENDING_EMAIL holds a requested address that has not been proven yet: the active
            // EMAIL column is only ever replaced by a successful verification.
            migrationBuilder.AddColumn<bool>(
                name: "EMAIL_VERIFIED",
                table: "USERS",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EMAIL_VERIFIED_AT",
                table: "USERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PENDING_EMAIL",
                table: "USERS",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EMAIL_VERIFICATION_TOKENS",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    USER_ID = table.Column<int>(type: "int", nullable: false),
                    PURPOSE = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TOKEN_HASH = table.Column<byte[]>(type: "varbinary(32)", nullable: false),
                    PREVIOUS_EMAIL = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    EXPIRES_AT = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CONSUMED_AT = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getdate())"),
                    REQUEST_IP = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EMAIL_VERIFICATION_TOKENS", x => x.ID);
                    table.ForeignKey(
                        name: "FK_EVT_USERS",
                        column: x => x.USER_ID,
                        principalTable: "USERS",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "USER_SECURITY_EVENTS",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    USER_ID = table.Column<int>(type: "int", nullable: false),
                    EVENT_TYPE = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ACTOR_ID = table.Column<int>(type: "int", nullable: true),
                    IP = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    DETAILS = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_SECURITY_EVENTS", x => x.ID);
                    table.ForeignKey(
                        name: "FK_USE_ACTOR",
                        column: x => x.ACTOR_ID,
                        principalTable: "USERS",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_USE_USERS",
                        column: x => x.USER_ID,
                        principalTable: "USERS",
                        principalColumn: "ID");
                });

            // No seed-data update is required, and none is issued: the three new USERS columns
            // are nullable and carry no default, so every existing row - the seeded system row
            // (ID = -1) included - keeps NULL, which is exactly the "pre-existing account,
            // verified under the previous rules" state the model snapshot records. Writing the
            // row again would touch a record that does not have to change, so this migration
            // leaves every existing row untouched.

            migrationBuilder.CreateIndex(
                name: "IX_EMAIL_VERIFICATION_TOKENS_USER",
                table: "EMAIL_VERIFICATION_TOKENS",
                columns: new[] { "USER_ID", "PURPOSE", "EXPIRES_AT" });

            migrationBuilder.CreateIndex(
                name: "UQ_EMAIL_VERIFICATION_TOKENS_HASH",
                table: "EMAIL_VERIFICATION_TOKENS",
                column: "TOKEN_HASH",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_USER_SECURITY_EVENTS_ACTOR_ID",
                table: "USER_SECURITY_EVENTS",
                column: "ACTOR_ID");

            migrationBuilder.CreateIndex(
                name: "IX_USER_SECURITY_EVENTS_TYPE",
                table: "USER_SECURITY_EVENTS",
                columns: new[] { "EVENT_TYPE", "CREATED_AT" });

            migrationBuilder.CreateIndex(
                name: "IX_USER_SECURITY_EVENTS_USER",
                table: "USER_SECURITY_EVENTS",
                columns: new[] { "USER_ID", "CREATED_AT" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Refuse to roll back while the feature is in use. Dropping the columns would turn
            // every unverified account into a legacy - implicitly verified - account, and it
            // would destroy the verification tokens and the security audit trail. The rollback
            // therefore fails loudly on purpose: recovery is forward-fix-first with a backup,
            // never an automatic Down (the same rule the Phase-1 migration follows).
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [USERS] WHERE [EMAIL_VERIFIED] = 0 OR [PENDING_EMAIL] IS NOT NULL)
BEGIN
    THROW 51000, 'Refusing to roll back the email-verification schema: unverified or pending accounts exist.', 1;
END;
IF EXISTS (SELECT 1 FROM [EMAIL_VERIFICATION_TOKENS])
BEGIN
    THROW 51001, 'Refusing to roll back the email-verification schema: verification tokens exist.', 1;
END;
IF EXISTS (SELECT 1 FROM [USER_SECURITY_EVENTS])
BEGIN
    THROW 51002, 'Refusing to roll back the email-verification schema: security audit events exist.', 1;
END;");

            migrationBuilder.DropTable(
                name: "EMAIL_VERIFICATION_TOKENS");

            migrationBuilder.DropTable(
                name: "USER_SECURITY_EVENTS");

            migrationBuilder.DropColumn(
                name: "EMAIL_VERIFIED",
                table: "USERS");

            migrationBuilder.DropColumn(
                name: "EMAIL_VERIFIED_AT",
                table: "USERS");

            migrationBuilder.DropColumn(
                name: "PENDING_EMAIL",
                table: "USERS");
        }
    }
}
