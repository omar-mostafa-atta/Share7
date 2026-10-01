using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MatchmakingTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MatchmakingTickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Size = table.Column<int>(type: "int", nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsRanked = table.Column<bool>(type: "bit", nullable: false),
                    ProtocolVersion = table.Column<int>(type: "int", nullable: false),
                    LangId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Region = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    RatingMu = table.Column<double>(type: "float", nullable: false),
                    RatingSigma = table.Column<double>(type: "float", nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    EnqueuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MatchedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HostUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EndReason = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    RequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchmakingTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchmakingTickets_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MatchmakingTicketLessons",
                columns: table => new
                {
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchmakingTicketLessons", x => new { x.TicketId, x.LessonId });
                    table.ForeignKey(
                        name: "FK_MatchmakingTicketLessons_MatchmakingTickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "MatchmakingTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchmakingTicketMembers",
                columns: table => new
                {
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsLive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchmakingTicketMembers", x => new { x.TicketId, x.UserId });
                    table.ForeignKey(
                        name: "FK_MatchmakingTicketMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MatchmakingTicketMembers_MatchmakingTickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "MatchmakingTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UQ_MatchmakingTicketMember_Live",
                table: "MatchmakingTicketMembers",
                column: "UserId",
                unique: true,
                filter: "[IsLive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_MatchmakingTicket_Owner",
                table: "MatchmakingTickets",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchmakingTicket_Searching",
                table: "MatchmakingTickets",
                columns: new[] { "State", "EnqueuedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchmakingTicket_Session",
                table: "MatchmakingTickets",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchmakingTicketLessons");

            migrationBuilder.DropTable(
                name: "MatchmakingTicketMembers");

            migrationBuilder.DropTable(
                name: "MatchmakingTickets");
        }
    }
}
