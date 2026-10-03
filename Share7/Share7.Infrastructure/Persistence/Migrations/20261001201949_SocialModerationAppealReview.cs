using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SocialModerationAppealReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AppealResolutionCode",
                table: "SocialRestrictions",
                type: "nvarchar(48)",
                maxLength: 48,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AppealReviewedAtUtc",
                table: "SocialRestrictions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PermanentRestriction",
                table: "PlayerReports",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RestrictDays",
                table: "PlayerReports",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AppealResolutionCode",
                table: "SocialRestrictions");

            migrationBuilder.DropColumn(
                name: "AppealReviewedAtUtc",
                table: "SocialRestrictions");

            migrationBuilder.DropColumn(
                name: "PermanentRestriction",
                table: "PlayerReports");

            migrationBuilder.DropColumn(
                name: "RestrictDays",
                table: "PlayerReports");
        }
    }
}
