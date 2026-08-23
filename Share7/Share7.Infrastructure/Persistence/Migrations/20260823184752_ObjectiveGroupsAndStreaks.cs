using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ObjectiveGroupsAndStreaks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "Objectives",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StepOrder",
                table: "Objectives",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ObjectiveGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CompletionMode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RequiredCount = table.Column<int>(type: "int", nullable: false),
                    SeasonKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AvailableFromUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AvailableToUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IconKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObjectiveGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserStreaks",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StreakKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Current = table.Column<int>(type: "int", nullable: false),
                    Best = table.Column<int>(type: "int", nullable: false),
                    LastCycleKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FreezesRemaining = table.Column<int>(type: "int", nullable: false),
                    FreezeRegeneratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserStreaks", x => new { x.UserId, x.StreakKey });
                    table.ForeignKey(
                        name: "FK_UserStreaks_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ObjectiveGroupTranslations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LangId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObjectiveGroupTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ObjectiveGroupTranslations_ObjectiveGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "ObjectiveGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserObjectiveGroupProgress",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CompletedCount = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClaimedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClaimableUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserObjectiveGroupProgress", x => new { x.UserId, x.GroupId, x.CycleKey });
                    table.ForeignKey(
                        name: "FK_UserObjectiveGroupProgress_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserObjectiveGroupProgress_ObjectiveGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "ObjectiveGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Objectives_GroupId",
                table: "Objectives",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ObjectiveGroups_Key",
                table: "ObjectiveGroups",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ObjectiveGroupTranslations_GroupId_LangId",
                table: "ObjectiveGroupTranslations",
                columns: new[] { "GroupId", "LangId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserObjectiveGroupProgress_GroupId",
                table: "UserObjectiveGroupProgress",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_UserObjectiveGroupProgress_User",
                table: "UserObjectiveGroupProgress",
                columns: new[] { "UserId", "State" });

            migrationBuilder.AddForeignKey(
                name: "FK_Objectives_ObjectiveGroups_GroupId",
                table: "Objectives",
                column: "GroupId",
                principalTable: "ObjectiveGroups",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Objectives_ObjectiveGroups_GroupId",
                table: "Objectives");

            migrationBuilder.DropTable(
                name: "ObjectiveGroupTranslations");

            migrationBuilder.DropTable(
                name: "UserObjectiveGroupProgress");

            migrationBuilder.DropTable(
                name: "UserStreaks");

            migrationBuilder.DropTable(
                name: "ObjectiveGroups");

            migrationBuilder.DropIndex(
                name: "IX_Objectives_GroupId",
                table: "Objectives");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "Objectives");

            migrationBuilder.DropColumn(
                name: "StepOrder",
                table: "Objectives");
        }
    }
}
