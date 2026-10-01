using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RankedRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRated",
                table: "MultiplayerSessions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Ranked",
                table: "GameModes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PlayerRatingChanges",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Placement = table.Column<int>(type: "int", nullable: false),
                    MuBefore = table.Column<double>(type: "float", nullable: false),
                    SigmaBefore = table.Column<double>(type: "float", nullable: false),
                    MuAfter = table.Column<double>(type: "float", nullable: false),
                    SigmaAfter = table.Column<double>(type: "float", nullable: false),
                    SkippedReason = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    SeasonKey = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Promoted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerRatingChanges", x => new { x.SessionId, x.UserId });
                    table.ForeignKey(
                        name: "FK_PlayerRatingChanges_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerRatings",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Mu = table.Column<double>(type: "float", nullable: false),
                    Sigma = table.Column<double>(type: "float", nullable: false),
                    MatchesPlayed = table.Column<int>(type: "int", nullable: false),
                    SeasonKey = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerRatings", x => new { x.UserId, x.ModeId });
                    table.ForeignKey(
                        name: "FK_PlayerRatings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerRatings_GameModes_ModeId",
                        column: x => x.ModeId,
                        principalTable: "GameModes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RankedSeasonStandings",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeasonKey = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    MatchesPlayed = table.Column<int>(type: "int", nullable: false),
                    Wins = table.Column<int>(type: "int", nullable: false),
                    PeakOrdinal = table.Column<double>(type: "float", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankedSeasonStandings", x => new { x.UserId, x.ModeId, x.SeasonKey });
                    table.ForeignKey(
                        name: "FK_RankedSeasonStandings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RankedSeasonStandings_GameModes_ModeId",
                        column: x => x.ModeId,
                        principalTable: "GameModes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerRatingChange_UserTime",
                table: "PlayerRatingChanges",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerRating_Mode",
                table: "PlayerRatings",
                column: "ModeId");

            migrationBuilder.CreateIndex(
                name: "IX_RankedSeasonStandings_ModeId",
                table: "RankedSeasonStandings",
                column: "ModeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerRatingChanges");

            migrationBuilder.DropTable(
                name: "PlayerRatings");

            migrationBuilder.DropTable(
                name: "RankedSeasonStandings");

            migrationBuilder.DropColumn(
                name: "IsRated",
                table: "MultiplayerSessions");

            migrationBuilder.DropColumn(
                name: "Ranked",
                table: "GameModes");
        }
    }
}
