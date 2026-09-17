using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiplayerSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MultiplayerRequestLogs",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MultiplayerRequestLogs", x => new { x.UserId, x.RequestId });
                    table.ForeignKey(
                        name: "FK_MultiplayerRequestLogs_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MultiplayerSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransportSessionName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TransportRegion = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    JoinCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Visibility = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    MaxPlayers = table.Column<int>(type: "int", nullable: false),
                    MinPlayers = table.Column<int>(type: "int", nullable: false),
                    CurrentPlayerCount = table.Column<int>(type: "int", nullable: false),
                    ProtocolVersion = table.Column<int>(type: "int", nullable: false),
                    CurriculumPathJson = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsRanked = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastHeartbeatAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedReason = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MultiplayerSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MultiplayerSessions_AspNetUsers_HostUserId",
                        column: x => x.HostUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MultiplayerSessions_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MultiplayerSessionPlayers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slot = table.Column<int>(type: "int", nullable: false),
                    IsHost = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    JoinedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeftAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MultiplayerSessionPlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MultiplayerSessionPlayers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MultiplayerSessionPlayers_MultiplayerSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "MultiplayerSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MultiplayerRequestLog_Retention",
                table: "MultiplayerRequestLogs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SessionPlayer_User",
                table: "MultiplayerSessionPlayers",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UQ_SessionPlayer_Active",
                table: "MultiplayerSessionPlayers",
                columns: new[] { "SessionId", "UserId" },
                unique: true,
                filter: "[Status] <> 'LEFT' AND [Status] <> 'REMOVED'");

            migrationBuilder.CreateIndex(
                name: "UQ_SessionPlayer_Slot",
                table: "MultiplayerSessionPlayers",
                columns: new[] { "SessionId", "Slot" },
                unique: true,
                filter: "[Status] <> 'LEFT' AND [Status] <> 'REMOVED'");

            migrationBuilder.CreateIndex(
                name: "IX_MultiplayerSession_Matchmaking",
                table: "MultiplayerSessions",
                columns: new[] { "GameId", "State", "Visibility", "IsRanked", "ProtocolVersion", "LessonId" })
                .Annotation("SqlServer:Include", new[] { "CurrentPlayerCount", "MaxPlayers", "LastHeartbeatAtUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MultiplayerSession_Sweep",
                table: "MultiplayerSessions",
                columns: new[] { "State", "LastHeartbeatAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MultiplayerSessions_HostUserId",
                table: "MultiplayerSessions",
                column: "HostUserId");

            migrationBuilder.CreateIndex(
                name: "UQ_MultiplayerSession_JoinCode",
                table: "MultiplayerSessions",
                column: "JoinCode",
                unique: true,
                filter: "[JoinCode] IS NOT NULL AND [State] <> 'CLOSED' AND [State] <> 'ABANDONED' AND [State] <> 'FAILED'");

            migrationBuilder.CreateIndex(
                name: "UQ_MultiplayerSession_Transport",
                table: "MultiplayerSessions",
                column: "TransportSessionName",
                unique: true,
                filter: "[State] <> 'CLOSED' AND [State] <> 'ABANDONED' AND [State] <> 'FAILED'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MultiplayerRequestLogs");

            migrationBuilder.DropTable(
                name: "MultiplayerSessionPlayers");

            migrationBuilder.DropTable(
                name: "MultiplayerSessions");
        }
    }
}
