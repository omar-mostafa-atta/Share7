using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GameCatalogAndProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LobbyScene = table.Column<int>(type: "int", nullable: false),
                    GameplayScene = table.Column<int>(type: "int", nullable: false),
                    MinPlayers = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    MaxPlayers = table.Column<int>(type: "int", nullable: false, defaultValue: 2),
                    ReadyTimeoutSeconds = table.Column<float>(type: "real", nullable: false, defaultValue: 20f),
                    SupportsSinglePlayer = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SupportsMultiplayer = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    UseLobby = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    UseMatchmaking = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameTranslations",
                columns: table => new
                {
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Lang_Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameTranslations", x => new { x.GameId, x.Lang_Id });
                    table.ForeignKey(
                        name: "FK_GameTranslations_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameTranslations_Languages_Lang_Id",
                        column: x => x.Lang_Id,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserLessonProgress",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CorrectCount = table.Column<int>(type: "int", nullable: false),
                    TotalCount = table.Column<int>(type: "int", nullable: false),
                    Percent = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    CompletionState = table.Column<int>(type: "int", nullable: false),
                    QuestionsVersion = table.Column<int>(type: "int", nullable: false),
                    FirstAttemptWasPerfect = table.Column<bool>(type: "bit", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLessonProgress", x => new { x.UserId, x.GameId, x.LessonId });
                    table.ForeignKey(
                        name: "FK_UserLessonProgress_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserLessonProgress_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserNodeUnlocks",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeType = table.Column<int>(type: "int", nullable: false),
                    NodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnlockedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNodeUnlocks", x => new { x.UserId, x.GameId, x.NodeType, x.NodeId });
                    table.ForeignKey(
                        name: "FK_UserNodeUnlocks_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserQuestionProgress",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserQuestionProgress", x => new { x.UserId, x.GameId, x.QuestionId });
                    table.ForeignKey(
                        name: "FK_UserQuestionProgress_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserQuestionProgress_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Games_GameKey",
                table: "Games",
                column: "GameKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameTranslations_Lang_Id",
                table: "GameTranslations",
                column: "Lang_Id");

            migrationBuilder.CreateIndex(
                name: "IX_UserLessonProgress_GameId",
                table: "UserLessonProgress",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLessonProgress_LessonId",
                table: "UserLessonProgress",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLessonProgress_UserId_GameId",
                table: "UserLessonProgress",
                columns: new[] { "UserId", "GameId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserNodeUnlocks_GameId",
                table: "UserNodeUnlocks",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNodeUnlocks_UserId_GameId",
                table: "UserNodeUnlocks",
                columns: new[] { "UserId", "GameId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserQuestionProgress_GameId",
                table: "UserQuestionProgress",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_UserQuestionProgress_QuestionId",
                table: "UserQuestionProgress",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserQuestionProgress_UserId_GameId_LessonId",
                table: "UserQuestionProgress",
                columns: new[] { "UserId", "GameId", "LessonId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameTranslations");

            migrationBuilder.DropTable(
                name: "UserLessonProgress");

            migrationBuilder.DropTable(
                name: "UserNodeUnlocks");

            migrationBuilder.DropTable(
                name: "UserQuestionProgress");

            migrationBuilder.DropTable(
                name: "Games");
        }
    }
}
