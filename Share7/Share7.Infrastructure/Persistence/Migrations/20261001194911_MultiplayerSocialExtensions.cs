using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiplayerSocialExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EligibilityReviewedAtUtc",
                table: "PrizeClaims",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FraudReviewedAtUtc",
                table: "PrizeClaims",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GuardianConfirmedAtUtc",
                table: "PrizeClaims",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GuardianLinkId",
                table: "PrizeClaims",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowObservers",
                table: "MultiplayerSessions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "DirectorySequence",
                table: "MultiplayerSessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.CreateTable(
                name: "GameObservationCapabilities",
                columns: table => new
                {
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProtocolVersion = table.Column<int>(type: "int", nullable: false),
                    ContractVersion = table.Column<int>(type: "int", nullable: false),
                    MaxObservers = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameObservationCapabilities", x => new { x.GameId, x.ProtocolVersion });
                    table.ForeignKey(
                        name: "FK_GameObservationCapabilities_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerTeams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerTeams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerTeams_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SessionObservers",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionObservers", x => new { x.SessionId, x.UserId });
                    table.ForeignKey(
                        name: "FK_SessionObservers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SessionObservers_MultiplayerSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "MultiplayerSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerTeamMembers",
                columns: table => new
                {
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Accepted = table.Column<bool>(type: "bit", nullable: false),
                    InvitedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerTeamMembers", x => new { x.TeamId, x.UserId });
                    table.ForeignKey(
                        name: "FK_PlayerTeamMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlayerTeamMembers_PlayerTeams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "PlayerTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Session_PublicDirectory",
                table: "MultiplayerSessions",
                columns: new[] { "Visibility", "State", "ProtocolVersion", "DirectorySequence" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTeamMembers_UserId",
                table: "PlayerTeamMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTeams_OwnerUserId_RequestId",
                table: "PlayerTeams",
                columns: new[] { "OwnerUserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionObservers_ExpiresAtUtc",
                table: "SessionObservers",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SessionObservers_UserId",
                table: "SessionObservers",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameObservationCapabilities");

            migrationBuilder.DropTable(
                name: "PlayerTeamMembers");

            migrationBuilder.DropTable(
                name: "SessionObservers");

            migrationBuilder.DropTable(
                name: "PlayerTeams");

            migrationBuilder.DropIndex(
                name: "IX_Session_PublicDirectory",
                table: "MultiplayerSessions");

            migrationBuilder.DropColumn(
                name: "EligibilityReviewedAtUtc",
                table: "PrizeClaims");

            migrationBuilder.DropColumn(
                name: "FraudReviewedAtUtc",
                table: "PrizeClaims");

            migrationBuilder.DropColumn(
                name: "GuardianConfirmedAtUtc",
                table: "PrizeClaims");

            migrationBuilder.DropColumn(
                name: "GuardianLinkId",
                table: "PrizeClaims");

            migrationBuilder.DropColumn(
                name: "AllowObservers",
                table: "MultiplayerSessions");

            migrationBuilder.DropColumn(
                name: "DirectorySequence",
                table: "MultiplayerSessions");
        }
    }
}
