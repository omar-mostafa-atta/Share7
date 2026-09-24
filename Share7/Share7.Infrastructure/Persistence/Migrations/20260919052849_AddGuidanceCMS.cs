using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGuidanceCMS : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GuidanceFlows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false, defaultValue: ""),
                    Kind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: "Tour"),
                    Priority = table.Column<int>(type: "int", nullable: false, defaultValue: 3),
                    ReplayPolicy = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    Skippable = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SkipAfterStep = table.Column<int>(type: "int", nullable: false, defaultValue: 2),
                    Resumable = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsKillSwitched = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ActiveVersionNumber = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    TargetAudienceJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuidanceFlows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GuidanceAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlowId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DetailsJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuidanceAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuidanceAuditLogs_GuidanceFlows_FlowId",
                        column: x => x.FlowId,
                        principalTable: "GuidanceFlows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GuidanceFlowVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlowId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "Draft"),
                    StepsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TriggerConditionsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangeSummary = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuidanceFlowVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuidanceFlowVersions_GuidanceFlows_FlowId",
                        column: x => x.FlowId,
                        principalTable: "GuidanceFlows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GuidanceAuditLogs_FlowId_TimestampUtc",
                table: "GuidanceAuditLogs",
                columns: new[] { "FlowId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GuidanceAuditLogs_TimestampUtc",
                table: "GuidanceAuditLogs",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_GuidanceFlows_IsKillSwitched",
                table: "GuidanceFlows",
                column: "IsKillSwitched");

            migrationBuilder.CreateIndex(
                name: "IX_GuidanceFlows_Key",
                table: "GuidanceFlows",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuidanceFlowVersions_FlowId_VersionNumber",
                table: "GuidanceFlowVersions",
                columns: new[] { "FlowId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuidanceFlowVersions_Status",
                table: "GuidanceFlowVersions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GuidanceAuditLogs");

            migrationBuilder.DropTable(
                name: "GuidanceFlowVersions");

            migrationBuilder.DropTable(
                name: "GuidanceFlows");
        }
    }
}
