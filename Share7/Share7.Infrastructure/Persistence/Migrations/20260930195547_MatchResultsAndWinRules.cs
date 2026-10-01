using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MatchResultsAndWinRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WinRuleJson",
                table: "GameModes",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MatchAttemptScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CorrectCount = table.Column<int>(type: "int", nullable: false),
                    TotalCount = table.Column<int>(type: "int", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchAttemptScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchAttemptScores_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchResults",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    WinRuleJson = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ParticipantCount = table.Column<int>(type: "int", nullable: false),
                    ReportedCount = table.Column<int>(type: "int", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    MatchStartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchResults", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "MatchPlacements",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slot = table.Column<int>(type: "int", nullable: false),
                    Placement = table.Column<int>(type: "int", nullable: true),
                    IsWinner = table.Column<bool>(type: "bit", nullable: false),
                    Forfeited = table.Column<bool>(type: "bit", nullable: false),
                    Flagged = table.Column<bool>(type: "bit", nullable: false),
                    FlagReason = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ValuesJson = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchPlacements", x => new { x.SessionId, x.UserId });
                    table.ForeignKey(
                        name: "FK_MatchPlacements_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchPlacements_MatchResults_SessionId",
                        column: x => x.SessionId,
                        principalTable: "MatchResults",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchAttemptScore_Session",
                table: "MatchAttemptScores",
                columns: new[] { "SessionId", "UserId", "SubmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchAttemptScores_UserId",
                table: "MatchAttemptScores",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchPlacement_User",
                table: "MatchPlacements",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchResult_DecidedAt",
                table: "MatchResults",
                column: "DecidedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchAttemptScores");

            migrationBuilder.DropTable(
                name: "MatchPlacements");

            migrationBuilder.DropTable(
                name: "MatchResults");

            migrationBuilder.DropColumn(
                name: "WinRuleJson",
                table: "GameModes");
        }
    }
}
