using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserGuidanceState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserGuidanceStates",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Generation = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    SessionOrdinal = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LastSessionDayUtc = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: ""),
                    StateJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompletedOnboarding = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    TotalFlowsCompleted = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGuidanceStates", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserGuidanceStates_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserGuidanceStates_CompletedOnboarding",
                table: "UserGuidanceStates",
                column: "CompletedOnboarding");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserGuidanceStates");
        }
    }
}
