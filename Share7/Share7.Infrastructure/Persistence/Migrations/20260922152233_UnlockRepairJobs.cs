using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnlockRepairJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UnlockRepairJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    NodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FormerOrder = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    StudentsRepaired = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnlockRepairJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserNodeUnlocks_NodeType_NodeId",
                table: "UserNodeUnlocks",
                columns: new[] { "NodeType", "NodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_UnlockRepairJobs_CreatedAtUtc",
                table: "UnlockRepairJobs",
                column: "CreatedAtUtc",
                filter: "[CompletedAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UnlockRepairJobs");

            migrationBuilder.DropIndex(
                name: "IX_UserNodeUnlocks_NodeType_NodeId",
                table: "UserNodeUnlocks");
        }
    }
}
