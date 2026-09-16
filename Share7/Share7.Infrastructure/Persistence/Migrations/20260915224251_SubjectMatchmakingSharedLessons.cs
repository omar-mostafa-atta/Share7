using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SubjectMatchmakingSharedLessons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EventId",
                table: "MultiplayerSessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LangId",
                table: "MultiplayerSessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModeId",
                table: "MultiplayerSessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubjectId",
                table: "MultiplayerSessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MultiplayerSessionEligibleLessons",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SummedBestPercent = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MultiplayerSessionEligibleLessons", x => new { x.SessionId, x.LessonId });
                    table.ForeignKey(
                        name: "FK_MultiplayerSessionEligibleLessons_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MultiplayerSessionEligibleLessons_MultiplayerSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "MultiplayerSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MultiplayerSession_SubjectMatchmaking",
                table: "MultiplayerSessions",
                columns: new[] { "GameId", "State", "Visibility", "IsRanked", "ProtocolVersion", "SubjectId", "LangId", "ModeId", "EventId" })
                .Annotation("SqlServer:Include", new[] { "CurrentPlayerCount", "MaxPlayers", "LastHeartbeatAtUtc", "CreatedAtUtc", "LessonId" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionEligibleLesson_Lesson",
                table: "MultiplayerSessionEligibleLessons",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionEligibleLesson_Pick",
                table: "MultiplayerSessionEligibleLessons",
                columns: new[] { "SessionId", "SummedBestPercent" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MultiplayerSessionEligibleLessons");

            migrationBuilder.DropIndex(
                name: "IX_MultiplayerSession_SubjectMatchmaking",
                table: "MultiplayerSessions");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "MultiplayerSessions");

            migrationBuilder.DropColumn(
                name: "LangId",
                table: "MultiplayerSessions");

            migrationBuilder.DropColumn(
                name: "ModeId",
                table: "MultiplayerSessions");

            migrationBuilder.DropColumn(
                name: "SubjectId",
                table: "MultiplayerSessions");
        }
    }
}
