using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiplayerRematchReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsReserved",
                table: "MultiplayerSessions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "RematchOfSessionId",
                table: "MultiplayerSessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MultiplayerSessionReservations",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MultiplayerSessionReservations", x => new { x.SessionId, x.UserId });
                    table.ForeignKey(
                        name: "FK_MultiplayerSessionReservations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MultiplayerSessionReservations_MultiplayerSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "MultiplayerSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UQ_MultiplayerSession_RematchOf",
                table: "MultiplayerSessions",
                column: "RematchOfSessionId",
                unique: true,
                filter: "[RematchOfSessionId] IS NOT NULL AND [State] <> 'CLOSED' AND [State] <> 'ABANDONED' AND [State] <> 'FAILED'");

            migrationBuilder.CreateIndex(
                name: "IX_SessionReservation_User",
                table: "MultiplayerSessionReservations",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MultiplayerSessionReservations");

            migrationBuilder.DropIndex(
                name: "UQ_MultiplayerSession_RematchOf",
                table: "MultiplayerSessions");

            migrationBuilder.DropColumn(
                name: "IsReserved",
                table: "MultiplayerSessions");

            migrationBuilder.DropColumn(
                name: "RematchOfSessionId",
                table: "MultiplayerSessions");
        }
    }
}
