using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RunBoundsReviewAndHardCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LayoutVersion",
                table: "Runs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReviewNote",
                table: "Runs",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAtUtc",
                table: "Runs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedByUserId",
                table: "Runs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DailyEarnCap",
                table: "Currencies",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHard",
                table: "Currencies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Run_Flagged",
                table: "Runs",
                columns: new[] { "IsFlagged", "ReviewedAtUtc", "EndedAtUtc" },
                filter: "[IsFlagged] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Run_UserSettled",
                table: "Runs",
                columns: new[] { "UserId", "State", "EndedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Run_Flagged",
                table: "Runs");

            migrationBuilder.DropIndex(
                name: "IX_Run_UserSettled",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "LayoutVersion",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "ReviewNote",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "ReviewedAtUtc",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "DailyEarnCap",
                table: "Currencies");

            migrationBuilder.DropColumn(
                name: "IsHard",
                table: "Currencies");
        }
    }
}
