using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlayContextModesWorldsEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 'CURRICULUM', not the generator's empty string. Every run already in this table was a
            // curriculum run — that was the only kind there was — and an empty token would leave
            // history that no SQL filter on the context could ever find.
            migrationBuilder.AddColumn<string>(
                name: "Context",
                table: "Runs",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "CURRICULUM");

            migrationBuilder.AddColumn<Guid>(
                name: "EventId",
                table: "Runs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModeId",
                table: "Runs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModeId",
                table: "LeaderboardMetricBounds",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EventId",
                table: "LeaderboardBoards",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModeId",
                table: "LeaderboardBoards",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Context",
                table: "GameResults",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "CURRICULUM");

            // **True, not the generator's false.** Every result already recorded was ranked, and a
            // false default would quietly unrank the entire history — including during a rebuild,
            // which replays this table and would hand back empty boards.
            migrationBuilder.AddColumn<bool>(
                name: "CountsForRanking",
                table: "GameResults",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EventId",
                table: "GameResults",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ModeId",
                table: "GameResults",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EconomyProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfileKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayoutPercent = table.Column<int>(type: "int", nullable: false),
                    PaysRuleRewards = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EconomyProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameWorlds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorldKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UnlockKind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MinLevel = table.Column<int>(type: "int", nullable: false),
                    MinGradeOrder = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameWorlds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameWorlds_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameWorlds_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GameModes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModeKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Topologies = table.Column<int>(type: "int", nullable: false),
                    MinPlayers = table.Column<int>(type: "int", nullable: false),
                    MaxPlayers = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    AvailableFromUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AvailableToUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequiresEntitlement = table.Column<bool>(type: "bit", nullable: false),
                    EntitlementProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MinGradeOrder = table.Column<int>(type: "int", nullable: false),
                    CountsTowardMastery = table.Column<bool>(type: "bit", nullable: false),
                    SettlesEconomy = table.Column<bool>(type: "bit", nullable: false),
                    CountsTowardRanking = table.Column<bool>(type: "bit", nullable: false),
                    EconomyProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameModes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameModes_EconomyProfiles_EconomyProfileId",
                        column: x => x.EconomyProfileId,
                        principalTable: "EconomyProfiles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GameModes_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameModes_Products_EntitlementProductId",
                        column: x => x.EntitlementProductId,
                        principalTable: "Products",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GameWorldTranslations",
                columns: table => new
                {
                    WorldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LangId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameWorldTranslations", x => new { x.WorldId, x.LangId });
                    table.ForeignKey(
                        name: "FK_GameWorldTranslations_GameWorlds_WorldId",
                        column: x => x.WorldId,
                        principalTable: "GameWorlds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameModeTranslations",
                columns: table => new
                {
                    ModeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LangId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameModeTranslations", x => new { x.ModeId, x.LangId });
                    table.ForeignKey(
                        name: "FK_GameModeTranslations_GameModes_ModeId",
                        column: x => x.ModeId,
                        principalTable: "GameModes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorldKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    GrantsWorldForDuration = table.Column<bool>(type: "bit", nullable: false),
                    BoardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrizeCohort = table.Column<int>(type: "int", nullable: false),
                    MaxEntriesPerDay = table.Column<int>(type: "int", nullable: true),
                    MaxEntriesTotal = table.Column<int>(type: "int", nullable: true),
                    MinGradeOrder = table.Column<int>(type: "int", nullable: false),
                    MaxGradeOrder = table.Column<int>(type: "int", nullable: false),
                    MinLevel = table.Column<int>(type: "int", nullable: false),
                    EntryProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClaimWindowDays = table.Column<int>(type: "int", nullable: false),
                    EconomyProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BannerAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AccentColor = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayEvents_EconomyProfiles_EconomyProfileId",
                        column: x => x.EconomyProfileId,
                        principalTable: "EconomyProfiles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlayEvents_GameModes_ModeId",
                        column: x => x.ModeId,
                        principalTable: "GameModes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlayEvents_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlayEvents_LeaderboardBoards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "LeaderboardBoards",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlayEvents_LeaderboardCycles_CycleId",
                        column: x => x.CycleId,
                        principalTable: "LeaderboardCycles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlayEvents_Products_EntryProductId",
                        column: x => x.EntryProductId,
                        principalTable: "Products",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EventPrizeTiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromRank = table.Column<int>(type: "int", nullable: false),
                    ToRank = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RewardRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeclaredValueMinor = table.Column<long>(type: "bigint", nullable: true),
                    ValueCurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventPrizeTiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventPrizeTiers_PlayEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "PlayEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventPrizeTiers_RewardRules_RewardRuleId",
                        column: x => x.RewardRuleId,
                        principalTable: "RewardRules",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PlayEventTranslations",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LangId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Rules = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayEventTranslations", x => new { x.EventId, x.LangId });
                    table.ForeignKey(
                        name: "FK_PlayEventTranslations_PlayEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "PlayEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventAwards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Cohort = table.Column<int>(type: "int", nullable: false),
                    CohortKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinalRank = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    RewardTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventAwards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventAwards_EventPrizeTiers_TierId",
                        column: x => x.TierId,
                        principalTable: "EventPrizeTiers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EventAwards_PlayEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "PlayEvents",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EventPrizeTierTranslations",
                columns: table => new
                {
                    TierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LangId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventPrizeTierTranslations", x => new { x.TierId, x.LangId });
                    table.ForeignKey(
                        name: "FK_EventPrizeTierTranslations_EventPrizeTiers_TierId",
                        column: x => x.TierId,
                        principalTable: "EventPrizeTiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrizeClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AwardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FulfilledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrizeClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrizeClaims_EventAwards_AwardId",
                        column: x => x.AwardId,
                        principalTable: "EventAwards",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Run_EventEntries",
                table: "Runs",
                columns: new[] { "EventId", "UserId", "State", "EndedAtUtc" },
                filter: "[EventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Runs_ModeId",
                table: "Runs",
                column: "ModeId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardBoards_ModeId",
                table: "LeaderboardBoards",
                column: "ModeId");

            migrationBuilder.CreateIndex(
                name: "UX_LeaderboardBoard_Event",
                table: "LeaderboardBoards",
                column: "EventId",
                unique: true,
                filter: "[EventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GameResult_Event",
                table: "GameResults",
                columns: new[] { "EventId", "Metric", "OccurredAtUtc" },
                filter: "[EventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_EconomyProfile_Default",
                table: "EconomyProfiles",
                column: "IsDefault",
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_EconomyProfile_Key",
                table: "EconomyProfiles",
                column: "ProfileKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventAward_User",
                table: "EventAwards",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EventAwards_TierId",
                table: "EventAwards",
                column: "TierId");

            migrationBuilder.CreateIndex(
                name: "UX_EventAward_Placing",
                table: "EventAwards",
                columns: new[] { "EventId", "Cohort", "CohortKey", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventPrizeTier_Event",
                table: "EventPrizeTiers",
                columns: new[] { "EventId", "FromRank" });

            migrationBuilder.CreateIndex(
                name: "IX_EventPrizeTiers_RewardRuleId",
                table: "EventPrizeTiers",
                column: "RewardRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_GameMode_Offered",
                table: "GameModes",
                columns: new[] { "GameId", "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_GameModes_EconomyProfileId",
                table: "GameModes",
                column: "EconomyProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_GameModes_EntitlementProductId",
                table: "GameModes",
                column: "EntitlementProductId");

            migrationBuilder.CreateIndex(
                name: "UX_GameMode_Default",
                table: "GameModes",
                column: "GameId",
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_GameMode_Key",
                table: "GameModes",
                column: "ModeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameWorld_Listing",
                table: "GameWorlds",
                columns: new[] { "GameId", "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_GameWorlds_ProductId",
                table: "GameWorlds",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "UX_GameWorld_Default",
                table: "GameWorlds",
                column: "GameId",
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_GameWorld_Key",
                table: "GameWorlds",
                columns: new[] { "GameId", "WorldKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayEvent_Listing",
                table: "PlayEvents",
                columns: new[] { "GameId", "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayEvents_BoardId",
                table: "PlayEvents",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayEvents_EconomyProfileId",
                table: "PlayEvents",
                column: "EconomyProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayEvents_EntryProductId",
                table: "PlayEvents",
                column: "EntryProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayEvents_ModeId",
                table: "PlayEvents",
                column: "ModeId");

            migrationBuilder.CreateIndex(
                name: "UX_PlayEvent_Cycle",
                table: "PlayEvents",
                column: "CycleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PlayEvent_Key",
                table: "PlayEvents",
                column: "EventKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrizeClaim_Expiry",
                table: "PrizeClaims",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PrizeClaim_Queue",
                table: "PrizeClaims",
                columns: new[] { "State", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_PrizeClaim_Award",
                table: "PrizeClaims",
                column: "AwardId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LeaderboardBoards_GameModes_ModeId",
                table: "LeaderboardBoards",
                column: "ModeId",
                principalTable: "GameModes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Runs_GameModes_ModeId",
                table: "Runs",
                column: "ModeId",
                principalTable: "GameModes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Runs_PlayEvents_EventId",
                table: "Runs",
                column: "EventId",
                principalTable: "PlayEvents",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LeaderboardBoards_GameModes_ModeId",
                table: "LeaderboardBoards");

            migrationBuilder.DropForeignKey(
                name: "FK_Runs_GameModes_ModeId",
                table: "Runs");

            migrationBuilder.DropForeignKey(
                name: "FK_Runs_PlayEvents_EventId",
                table: "Runs");

            migrationBuilder.DropTable(
                name: "EventPrizeTierTranslations");

            migrationBuilder.DropTable(
                name: "GameModeTranslations");

            migrationBuilder.DropTable(
                name: "GameWorldTranslations");

            migrationBuilder.DropTable(
                name: "PlayEventTranslations");

            migrationBuilder.DropTable(
                name: "PrizeClaims");

            migrationBuilder.DropTable(
                name: "GameWorlds");

            migrationBuilder.DropTable(
                name: "EventAwards");

            migrationBuilder.DropTable(
                name: "EventPrizeTiers");

            migrationBuilder.DropTable(
                name: "PlayEvents");

            migrationBuilder.DropTable(
                name: "GameModes");

            migrationBuilder.DropTable(
                name: "EconomyProfiles");

            migrationBuilder.DropIndex(
                name: "IX_Run_EventEntries",
                table: "Runs");

            migrationBuilder.DropIndex(
                name: "IX_Runs_ModeId",
                table: "Runs");

            migrationBuilder.DropIndex(
                name: "IX_LeaderboardBoards_ModeId",
                table: "LeaderboardBoards");

            migrationBuilder.DropIndex(
                name: "UX_LeaderboardBoard_Event",
                table: "LeaderboardBoards");

            migrationBuilder.DropIndex(
                name: "IX_GameResult_Event",
                table: "GameResults");

            migrationBuilder.DropColumn(
                name: "Context",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "ModeId",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "ModeId",
                table: "LeaderboardMetricBounds");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "LeaderboardBoards");

            migrationBuilder.DropColumn(
                name: "ModeId",
                table: "LeaderboardBoards");

            migrationBuilder.DropColumn(
                name: "Context",
                table: "GameResults");

            migrationBuilder.DropColumn(
                name: "CountsForRanking",
                table: "GameResults");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "GameResults");

            migrationBuilder.DropColumn(
                name: "ModeId",
                table: "GameResults");
        }
    }
}
