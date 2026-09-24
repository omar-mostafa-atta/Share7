using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StaffAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaffProfiles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    JobTitle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WorkEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    StudioRole = table.Column<int>(type: "int", nullable: false),
                    AllNodes = table.Column<bool>(type: "bit", nullable: false),
                    AllLanguages = table.Column<bool>(type: "bit", nullable: false),
                    InterfaceLanguage = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActivatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StatusChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StatusChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StatusReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    LastActiveAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastTwoStepCodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LastTwoStepAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffProfiles", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_StaffProfiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StaffSecuritySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    RequireTwoStep = table.Column<bool>(type: "bit", nullable: false),
                    SessionLifetimeHours = table.Column<int>(type: "int", nullable: false),
                    IdleTimeoutHours = table.Column<int>(type: "int", nullable: false),
                    MinimumPasswordLength = table.Column<int>(type: "int", nullable: false),
                    SetupLinkLifetimeHours = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffSecuritySettings", x => x.Id);
                    table.CheckConstraint("CK_StaffSecuritySettings_Singleton", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "StaffSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RefreshTokenHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    PreviousRefreshTokenHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true),
                    RotatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    TwoStepVerified = table.Column<bool>(type: "bit", nullable: false),
                    StampHash = table.Column<string>(type: "varchar(22)", unicode: false, maxLength: 22, nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedReason = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffSessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StaffSetupTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffSetupTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffSetupTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StaffSignInEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffSignInEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffSignInEvents_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StaffScopeLanguages",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LanguageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffScopeLanguages", x => new { x.UserId, x.LanguageId });
                    table.ForeignKey(
                        name: "FK_StaffScopeLanguages_Languages_LanguageId",
                        column: x => x.LanguageId,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffScopeLanguages_StaffProfiles_UserId",
                        column: x => x.UserId,
                        principalTable: "StaffProfiles",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StaffScopeNodes",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffScopeNodes", x => new { x.UserId, x.NodeId });
                    table.ForeignKey(
                        name: "FK_StaffScopeNodes_StaffProfiles_UserId",
                        column: x => x.UserId,
                        principalTable: "StaffProfiles",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "StaffSecuritySettings",
                columns: new[] { "Id", "IdleTimeoutHours", "MinimumPasswordLength", "RequireTwoStep", "SessionLifetimeHours", "SetupLinkLifetimeHours", "UpdatedAtUtc", "UpdatedByUserId" },
                values: new object[] { 1, 12, 12, false, 168, 72, new DateTime(2026, 9, 22, 0, 0, 0, 0, DateTimeKind.Utc), null });

            migrationBuilder.CreateIndex(
                name: "IX_StaffProfiles_Status",
                table: "StaffProfiles",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_StaffScopeLanguages_LanguageId",
                table: "StaffScopeLanguages",
                column: "LanguageId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffScopeNodes_NodeId",
                table: "StaffScopeNodes",
                column: "NodeId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffSessions_PreviousRefreshTokenHash",
                table: "StaffSessions",
                column: "PreviousRefreshTokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_StaffSessions_RefreshTokenHash",
                table: "StaffSessions",
                column: "RefreshTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffSessions_UserId_RevokedAtUtc",
                table: "StaffSessions",
                columns: new[] { "UserId", "RevokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffSetupTokens_TokenHash",
                table: "StaffSetupTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffSetupTokens_UserId_CreatedAtUtc",
                table: "StaffSetupTokens",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffSignInEvents_UserId_OccurredAtUtc",
                table: "StaffSignInEvents",
                columns: new[] { "UserId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffScopeLanguages");

            migrationBuilder.DropTable(
                name: "StaffScopeNodes");

            migrationBuilder.DropTable(
                name: "StaffSecuritySettings");

            migrationBuilder.DropTable(
                name: "StaffSessions");

            migrationBuilder.DropTable(
                name: "StaffSetupTokens");

            migrationBuilder.DropTable(
                name: "StaffSignInEvents");

            migrationBuilder.DropTable(
                name: "StaffProfiles");
        }
    }
}
