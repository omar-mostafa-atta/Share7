using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeaderboardPlausibilityBounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeaderboardMetricBounds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Metric = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    MaxValue = table.Column<long>(type: "bigint", nullable: true),
                    MaxResultsPerDay = table.Column<int>(type: "int", nullable: true),
                    MaxValuePerDay = table.Column<long>(type: "bigint", nullable: true),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaderboardMetricBounds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaderboardMetricBounds_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardMetricBound_Lookup",
                table: "LeaderboardMetricBounds",
                columns: new[] { "Metric", "Enabled" });

            migrationBuilder.CreateIndex(
                name: "UX_LeaderboardMetricBound_Scope",
                table: "LeaderboardMetricBounds",
                columns: new[] { "GameId", "Metric" },
                unique: true,
                filter: "[GameId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeaderboardMetricBounds");
        }
    }
}
