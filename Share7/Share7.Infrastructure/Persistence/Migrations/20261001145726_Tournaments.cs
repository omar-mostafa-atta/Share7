using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Tournaments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TournamentMatchId",
                table: "MultiplayerSessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Tournaments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CohortId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Format = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    SwissRounds = table.Column<int>(type: "int", nullable: false),
                    MaxEntrants = table.Column<int>(type: "int", nullable: false),
                    EntrantCount = table.Column<int>(type: "int", nullable: false),
                    MatchMinutes = table.Column<int>(type: "int", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrentRound = table.Column<int>(type: "int", nullable: false),
                    RoundCount = table.Column<int>(type: "int", nullable: false),
                    RandomSeed = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AdvancedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PrizesAwardedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tournaments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TournamentEntries",
                columns: table => new
                {
                    TournamentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Seed = table.Column<int>(type: "int", nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false),
                    Wins = table.Column<int>(type: "int", nullable: false),
                    Losses = table.Column<int>(type: "int", nullable: false),
                    Draws = table.Column<int>(type: "int", nullable: false),
                    Byes = table.Column<int>(type: "int", nullable: false),
                    MissedMatches = table.Column<int>(type: "int", nullable: false),
                    EliminatedInRound = table.Column<int>(type: "int", nullable: true),
                    Placement = table.Column<int>(type: "int", nullable: true),
                    RegisteredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeftAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentEntries", x => new { x.TournamentId, x.UserId });
                    table.ForeignKey(
                        name: "FK_TournamentEntries_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TournamentEntries_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TournamentMatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TournamentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    PlayerAUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PlayerBUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    GameNumber = table.Column<int>(type: "int", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ACheckedInAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BCheckedInAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReadyAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeadlineAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WinnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Flagged = table.Column<bool>(type: "bit", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TournamentMatches_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UQ_MultiplayerSession_TournamentMatch",
                table: "MultiplayerSessions",
                column: "TournamentMatchId",
                unique: true,
                filter: "[TournamentMatchId] IS NOT NULL AND [State] <> 'CLOSED' AND [State] <> 'ABANDONED' AND [State] <> 'FAILED'");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentEntry_User",
                table: "TournamentEntries",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatch_PlayerA",
                table: "TournamentMatches",
                column: "PlayerAUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatch_PlayerB",
                table: "TournamentMatches",
                column: "PlayerBUserId");

            migrationBuilder.CreateIndex(
                name: "UQ_TournamentMatch_Slot",
                table: "TournamentMatches",
                columns: new[] { "TournamentId", "Round", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tournament_Cohort",
                table: "Tournaments",
                column: "CohortId");

            migrationBuilder.CreateIndex(
                name: "IX_Tournament_Event",
                table: "Tournaments",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_Tournament_State",
                table: "Tournaments",
                columns: new[] { "State", "StartsAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TournamentEntries");

            migrationBuilder.DropTable(
                name: "TournamentMatches");

            migrationBuilder.DropTable(
                name: "Tournaments");

            migrationBuilder.DropIndex(
                name: "UQ_MultiplayerSession_TournamentMatch",
                table: "MultiplayerSessions");

            migrationBuilder.DropColumn(
                name: "TournamentMatchId",
                table: "MultiplayerSessions");
        }
    }
}
