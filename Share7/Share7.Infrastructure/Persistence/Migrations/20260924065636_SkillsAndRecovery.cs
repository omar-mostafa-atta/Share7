using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SkillsAndRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EditedAtUtc",
                table: "LearningTargets",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EditedByUserId",
                table: "LearningTargets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecoveryRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopePath = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    NodeKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AfterWrongAnswers = table.Column<int>(type: "int", nullable: false),
                    QuestionsToServe = table.Column<int>(type: "int", nullable: false),
                    AllowRepeats = table.Column<bool>(type: "bit", nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WrittenByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SupersededAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryRules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryRules_IsActive_ScopePath",
                table: "RecoveryRules",
                columns: new[] { "IsActive", "ScopePath" });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryRules_NodeId",
                table: "RecoveryRules",
                column: "NodeId",
                unique: true,
                filter: "[IsActive] = 1 AND [TargetId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryRules_ReleaseId",
                table: "RecoveryRules",
                column: "ReleaseId",
                filter: "[ReleaseId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryRules_TargetId",
                table: "RecoveryRules",
                column: "TargetId",
                filter: "[TargetId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecoveryRules");

            migrationBuilder.DropColumn(
                name: "EditedAtUtc",
                table: "LearningTargets");

            migrationBuilder.DropColumn(
                name: "EditedByUserId",
                table: "LearningTargets");
        }
    }
}
