using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeaderboardSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Metric = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SourceType = table.Column<int>(type: "int", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    GradeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LangId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsFlagged = table.Column<bool>(type: "bit", nullable: false),
                    FlagReason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ProjectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameResults_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameResults_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LeaderboardBoards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BoardKey = table.Column<string>(type: "nvarchar(110)", maxLength: 110, nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Metric = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    SortDirection = table.Column<int>(type: "int", nullable: false),
                    Aggregation = table.Column<int>(type: "int", nullable: false),
                    Period = table.Column<int>(type: "int", nullable: false),
                    SupportedCohorts = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    GradeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LangId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VisibleRankLimit = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    GraceSeconds = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaderboardBoards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaderboardBoards_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LeaderboardJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BoardId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    RunAfterUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClaimedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClaimExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaderboardJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerDisplayNames",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Handle = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    IsHidden = table.Column<bool>(type: "bit", nullable: false),
                    IsHiddenByGuardian = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerDisplayNames", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_PlayerDisplayNames_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeaderboardBoardTranslations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BoardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LangId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaderboardBoardTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaderboardBoardTranslations_Languages_LangId",
                        column: x => x.LangId,
                        principalTable: "Languages",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LeaderboardBoardTranslations_LeaderboardBoards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "LeaderboardBoards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeaderboardCycles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BoardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SettledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalRanked = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaderboardCycles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaderboardCycles_LeaderboardBoards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "LeaderboardBoards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeaderboardEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Cohort = table.Column<int>(type: "int", nullable: false),
                    CohortKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    AchievedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    AvatarKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsHidden = table.Column<bool>(type: "bit", nullable: false),
                    IsFlagged = table.Column<bool>(type: "bit", nullable: false),
                    LastResultId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaderboardEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaderboardEntries_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LeaderboardEntries_LeaderboardCycles_CycleId",
                        column: x => x.CycleId,
                        principalTable: "LeaderboardCycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeaderboardSettlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Cohort = table.Column<int>(type: "int", nullable: false),
                    CohortKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinalRank = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    RewardReferenceKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RewardIssued = table.Column<bool>(type: "bit", nullable: false),
                    RewardIssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaderboardSettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaderboardSettlements_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LeaderboardSettlements_LeaderboardCycles_CycleId",
                        column: x => x.CycleId,
                        principalTable: "LeaderboardCycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameResult_Pending",
                table: "GameResults",
                columns: new[] { "ProjectedAtUtc", "OccurredAtUtc" },
                filter: "[ProjectedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GameResult_Replay",
                table: "GameResults",
                columns: new[] { "GameId", "Metric", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GameResult_User",
                table: "GameResults",
                columns: new[] { "UserId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_GameResult_Submission",
                table: "GameResults",
                columns: new[] { "UserId", "RequestId", "Metric" },
                unique: true,
                filter: "[RequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardBoard_Listing",
                table: "LeaderboardBoards",
                columns: new[] { "IsActive", "GameId" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardBoards_BoardKey",
                table: "LeaderboardBoards",
                column: "BoardKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardBoards_GameId",
                table: "LeaderboardBoards",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardBoardTranslations_BoardId_LangId",
                table: "LeaderboardBoardTranslations",
                columns: new[] { "BoardId", "LangId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardBoardTranslations_LangId",
                table: "LeaderboardBoardTranslations",
                column: "LangId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardCycle_Board_State",
                table: "LeaderboardCycles",
                columns: new[] { "BoardId", "State", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardCycle_Rollover",
                table: "LeaderboardCycles",
                columns: new[] { "State", "EndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_LeaderboardCycle_Window",
                table: "LeaderboardCycles",
                columns: new[] { "BoardId", "StartsAtUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardEntries_UserId",
                table: "LeaderboardEntries",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardEntry_Ordering",
                table: "LeaderboardEntries",
                columns: new[] { "CycleId", "Cohort", "CohortKey", "Value", "AchievedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardEntry_Page",
                table: "LeaderboardEntries",
                columns: new[] { "CycleId", "Cohort", "CohortKey", "Rank" });

            migrationBuilder.CreateIndex(
                name: "UX_LeaderboardEntry_Member",
                table: "LeaderboardEntries",
                columns: new[] { "CycleId", "Cohort", "CohortKey", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardJob_Claimable",
                table: "LeaderboardJobs",
                columns: new[] { "State", "RunAfterUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_LeaderboardJob_Outstanding",
                table: "LeaderboardJobs",
                columns: new[] { "Kind", "CycleId" },
                unique: true,
                filter: "[State] IN (0, 1) AND [CycleId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardSettlement_User",
                table: "LeaderboardSettlements",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_LeaderboardSettlement_Award",
                table: "LeaderboardSettlements",
                columns: new[] { "CycleId", "Cohort", "CohortKey", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerDisplayNames_Handle",
                table: "PlayerDisplayNames",
                column: "Handle",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameResults");

            migrationBuilder.DropTable(
                name: "LeaderboardBoardTranslations");

            migrationBuilder.DropTable(
                name: "LeaderboardEntries");

            migrationBuilder.DropTable(
                name: "LeaderboardJobs");

            migrationBuilder.DropTable(
                name: "LeaderboardSettlements");

            migrationBuilder.DropTable(
                name: "PlayerDisplayNames");

            migrationBuilder.DropTable(
                name: "LeaderboardCycles");

            migrationBuilder.DropTable(
                name: "LeaderboardBoards");
        }
    }
}
