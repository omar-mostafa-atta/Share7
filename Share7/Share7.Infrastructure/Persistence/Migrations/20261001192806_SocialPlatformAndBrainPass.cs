using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SocialPlatformAndBrainPass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrainPassSeasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClaimUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    PremiumProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ObjectiveGroupKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrainPassSeasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BrainPassSeasons_Products_PremiumProductId",
                        column: x => x.PremiumProductId,
                        principalTable: "Products",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InboxPreferences",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxPreferences", x => new { x.UserId, x.Category });
                    table.ForeignKey(
                        name: "FK_InboxPreferences_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InboxReads",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxReads", x => new { x.UserId, x.EventId });
                    table.ForeignKey(
                        name: "FK_InboxReads_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OfficialActivities",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TitleEn = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    TitleAr = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PublicationKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialActivities", x => x.Sequence);
                    table.ForeignKey(
                        name: "FK_OfficialActivities_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OfficialFollows",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FollowedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialFollows", x => new { x.UserId, x.FollowedUserId });
                    table.ForeignKey(
                        name: "FK_OfficialFollows_AspNetUsers_FollowedUserId",
                        column: x => x.FollowedUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OfficialFollows_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OfficialProfiles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Verified = table.Column<bool>(type: "bit", nullable: false),
                    Discoverable = table.Column<bool>(type: "bit", nullable: false),
                    DisplayNameEn = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DisplayNameAr = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    TitleEn = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    TitleAr = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialProfiles", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_OfficialProfiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerMutes",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MutedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerMutes", x => new { x.UserId, x.MutedUserId });
                    table.ForeignKey(
                        name: "FK_PlayerMutes_AspNetUsers_MutedUserId",
                        column: x => x.MutedUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlayerMutes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PlayerReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EvidenceJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    DecisionCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerReports_AspNetUsers_ReportedUserId",
                        column: x => x.ReportedUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlayerReports_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PlayerShowcases",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SelectionJson = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerShowcases", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_PlayerShowcases_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessionArchives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ParticipantCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionArchives", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShowcaseContents",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    AssetKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    FallbackKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OfficialOnly = table.Column<bool>(type: "bit", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    MinimumQuality = table.Column<int>(type: "int", nullable: false),
                    ContractVersion = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShowcaseContents", x => x.Key);
                    table.ForeignKey(
                        name: "FK_ShowcaseContents_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ShowcaseContents_ShowcaseContents_FallbackKey",
                        column: x => x.FallbackKey,
                        principalTable: "ShowcaseContents",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "SocialPrivacy",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Profile = table.Column<int>(type: "int", nullable: false),
                    Presence = table.Column<int>(type: "int", nullable: false),
                    Statistics = table.Column<int>(type: "int", nullable: false),
                    Invitations = table.Column<int>(type: "int", nullable: false),
                    Challenges = table.Column<int>(type: "int", nullable: false),
                    FriendRequests = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialPrivacy", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_SocialPrivacy_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SocialRestrictions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AppealCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    AppealedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialRestrictions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocialRestrictions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BrainPassClaims",
                columns: table => new
                {
                    SeasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tier = table.Column<int>(type: "int", nullable: false),
                    Track = table.Column<int>(type: "int", nullable: false),
                    RewardsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    ClaimedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrainPassClaims", x => new { x.SeasonId, x.UserId, x.Tier, x.Track });
                    table.ForeignKey(
                        name: "FK_BrainPassClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BrainPassClaims_BrainPassSeasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "BrainPassSeasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BrainPassCredits",
                columns: table => new
                {
                    SeasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Xp = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrainPassCredits", x => new { x.SeasonId, x.ResultId });
                    table.ForeignKey(
                        name: "FK_BrainPassCredits_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BrainPassCredits_BrainPassSeasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "BrainPassSeasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BrainPassDaily",
                columns: table => new
                {
                    SeasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Metric = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DayUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Xp = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrainPassDaily", x => new { x.SeasonId, x.UserId, x.Metric, x.DayUtc });
                    table.ForeignKey(
                        name: "FK_BrainPassDaily_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BrainPassDaily_BrainPassSeasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "BrainPassSeasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BrainPassProgress",
                columns: table => new
                {
                    SeasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Xp = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrainPassProgress", x => new { x.SeasonId, x.UserId });
                    table.ForeignKey(
                        name: "FK_BrainPassProgress_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BrainPassProgress_BrainPassSeasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "BrainPassSeasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BrainPassSources",
                columns: table => new
                {
                    SeasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Metric = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaximumValue = table.Column<long>(type: "bigint", nullable: false),
                    CreditedXp = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrainPassSources", x => new { x.SeasonId, x.UserId, x.Metric, x.SourceId });
                    table.ForeignKey(
                        name: "FK_BrainPassSources_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BrainPassSources_BrainPassSeasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "BrainPassSeasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BrainPassTiers",
                columns: table => new
                {
                    SeasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Track = table.Column<int>(type: "int", nullable: false),
                    RequiredXp = table.Column<long>(type: "bigint", nullable: false),
                    RewardRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrainPassTiers", x => new { x.SeasonId, x.Number, x.Track });
                    table.ForeignKey(
                        name: "FK_BrainPassTiers_BrainPassSeasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "BrainPassSeasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BrainPassTiers_RewardRules_RewardRuleId",
                        column: x => x.RewardRuleId,
                        principalTable: "RewardRules",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BrainPassXpRules",
                columns: table => new
                {
                    SeasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Metric = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MinimumValue = table.Column<long>(type: "bigint", nullable: false),
                    UnitValue = table.Column<long>(type: "bigint", nullable: false),
                    XpPerUnit = table.Column<int>(type: "int", nullable: false),
                    MaxSourceXp = table.Column<int>(type: "int", nullable: false),
                    DailyCap = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrainPassXpRules", x => new { x.SeasonId, x.Metric });
                    table.ForeignKey(
                        name: "FK_BrainPassXpRules_BrainPassSeasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "BrainPassSeasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessionArchiveParticipants",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Placement = table.Column<int>(type: "int", nullable: true),
                    IsWinner = table.Column<bool>(type: "bit", nullable: false),
                    Forfeited = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionArchiveParticipants", x => new { x.SessionId, x.UserId });
                    table.ForeignKey(
                        name: "FK_SessionArchiveParticipants_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SessionArchiveParticipants_SessionArchives_SessionId",
                        column: x => x.SessionId,
                        principalTable: "SessionArchives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrainPassClaims_UserId",
                table: "BrainPassClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BrainPassCredits_UserId_SeasonId",
                table: "BrainPassCredits",
                columns: new[] { "UserId", "SeasonId" });

            migrationBuilder.CreateIndex(
                name: "IX_BrainPassDaily_UserId",
                table: "BrainPassDaily",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BrainPassProgress_UserId",
                table: "BrainPassProgress",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BrainPassSeasons_Key",
                table: "BrainPassSeasons",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BrainPassSeasons_PremiumProductId",
                table: "BrainPassSeasons",
                column: "PremiumProductId");

            migrationBuilder.CreateIndex(
                name: "IX_BrainPassSeasons_State_StartsAtUtc_EndsAtUtc",
                table: "BrainPassSeasons",
                columns: new[] { "State", "StartsAtUtc", "EndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BrainPassSources_UserId",
                table: "BrainPassSources",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BrainPassTiers_RewardRuleId",
                table: "BrainPassTiers",
                column: "RewardRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_InboxReads_ReadAtUtc",
                table: "InboxReads",
                column: "ReadAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OfficialActivities_ExpiresAtUtc",
                table: "OfficialActivities",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OfficialActivities_UserId_PublicationKey",
                table: "OfficialActivities",
                columns: new[] { "UserId", "PublicationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfficialActivities_UserId_Sequence",
                table: "OfficialActivities",
                columns: new[] { "UserId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_OfficialFollows_FollowedUserId_UserId",
                table: "OfficialFollows",
                columns: new[] { "FollowedUserId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_OfficialProfiles_Discoverable_Sequence",
                table: "OfficialProfiles",
                columns: new[] { "Discoverable", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_OfficialProfiles_Sequence",
                table: "OfficialProfiles",
                column: "Sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMutes_MutedUserId",
                table: "PlayerMutes",
                column: "MutedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerReports_ReportedUserId",
                table: "PlayerReports",
                column: "ReportedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerReports_Sequence",
                table: "PlayerReports",
                column: "Sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerReports_State_Sequence",
                table: "PlayerReports",
                columns: new[] { "State", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerReports_UserId_CreatedAtUtc",
                table: "PlayerReports",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerReports_UserId_RequestId",
                table: "PlayerReports",
                columns: new[] { "UserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionArchiveParticipants_UserId",
                table: "SessionArchiveParticipants",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionArchives_ExpiresAtUtc",
                table: "SessionArchives",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ShowcaseContents_Enabled_Kind_Key",
                table: "ShowcaseContents",
                columns: new[] { "Enabled", "Kind", "Key" });

            migrationBuilder.CreateIndex(
                name: "IX_ShowcaseContents_FallbackKey",
                table: "ShowcaseContents",
                column: "FallbackKey");

            migrationBuilder.CreateIndex(
                name: "IX_ShowcaseContents_ProductId",
                table: "ShowcaseContents",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialRestrictions_UserId_RevokedAtUtc_ExpiresAtUtc",
                table: "SocialRestrictions",
                columns: new[] { "UserId", "RevokedAtUtc", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrainPassClaims");

            migrationBuilder.DropTable(
                name: "BrainPassCredits");

            migrationBuilder.DropTable(
                name: "BrainPassDaily");

            migrationBuilder.DropTable(
                name: "BrainPassProgress");

            migrationBuilder.DropTable(
                name: "BrainPassSources");

            migrationBuilder.DropTable(
                name: "BrainPassTiers");

            migrationBuilder.DropTable(
                name: "BrainPassXpRules");

            migrationBuilder.DropTable(
                name: "InboxPreferences");

            migrationBuilder.DropTable(
                name: "InboxReads");

            migrationBuilder.DropTable(
                name: "OfficialActivities");

            migrationBuilder.DropTable(
                name: "OfficialFollows");

            migrationBuilder.DropTable(
                name: "OfficialProfiles");

            migrationBuilder.DropTable(
                name: "PlayerMutes");

            migrationBuilder.DropTable(
                name: "PlayerReports");

            migrationBuilder.DropTable(
                name: "PlayerShowcases");

            migrationBuilder.DropTable(
                name: "SessionArchiveParticipants");

            migrationBuilder.DropTable(
                name: "ShowcaseContents");

            migrationBuilder.DropTable(
                name: "SocialPrivacy");

            migrationBuilder.DropTable(
                name: "SocialRestrictions");

            migrationBuilder.DropTable(
                name: "BrainPassSeasons");

            migrationBuilder.DropTable(
                name: "SessionArchives");
        }
    }
}
