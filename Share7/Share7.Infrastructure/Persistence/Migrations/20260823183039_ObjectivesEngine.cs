using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ObjectivesEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Objectives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Metric = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Target = table.Column<long>(type: "bigint", nullable: false),
                    Aggregation = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GradeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LangId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_Objectives", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectionCheckpoints",
                columns: table => new
                {
                    Consumer = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Watermark = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectionCheckpoints", x => x.Consumer);
                });

            migrationBuilder.CreateTable(
                name: "ObjectiveTranslations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectiveId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LangId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObjectiveTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ObjectiveTranslations_Objectives_ObjectiveId",
                        column: x => x.ObjectiveId,
                        principalTable: "Objectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserObjectiveProgress",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectiveId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClaimedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClaimableUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserObjectiveProgress", x => new { x.UserId, x.ObjectiveId, x.CycleKey });
                    table.ForeignKey(
                        name: "FK_UserObjectiveProgress_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserObjectiveProgress_Objectives_ObjectiveId",
                        column: x => x.ObjectiveId,
                        principalTable: "Objectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Objective_Active",
                table: "Objectives",
                columns: new[] { "IsActive", "Metric" });

            migrationBuilder.CreateIndex(
                name: "IX_Objectives_Key",
                table: "Objectives",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ObjectiveTranslations_ObjectiveId_LangId",
                table: "ObjectiveTranslations",
                columns: new[] { "ObjectiveId", "LangId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserObjectiveProgress_ObjectiveId",
                table: "UserObjectiveProgress",
                column: "ObjectiveId");

            migrationBuilder.CreateIndex(
                name: "IX_UserObjectiveProgress_User",
                table: "UserObjectiveProgress",
                columns: new[] { "UserId", "State" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ObjectiveTranslations");

            migrationBuilder.DropTable(
                name: "ProjectionCheckpoints");

            migrationBuilder.DropTable(
                name: "UserObjectiveProgress");

            migrationBuilder.DropTable(
                name: "Objectives");
        }
    }
}
