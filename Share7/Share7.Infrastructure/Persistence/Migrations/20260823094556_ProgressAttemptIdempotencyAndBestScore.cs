using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProgressAttemptIdempotencyAndBestScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BestPercent",
                table: "UserLessonProgress",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Backfill, and it is not optional: left at 0, the next attempt on any existing lesson
            // would compute a record state of Uncompleted and wipe progress every student already
            // earned. The old rows only ever stored the *last* attempt, so the best is reconstructed
            // from what the last attempt and the state it left behind together imply.
            //
            // This also recovers aces the last-attempt bug destroyed: a row with
            // FirstAttemptWasPerfect = 1 was a 100% run whatever the state was demoted to since.
            //
            // Written as nested CASE rather than GREATEST — the latter is SQL Server 2022+ and the
            // deployed edition on shared hosting is not confirmed.
            migrationBuilder.Sql(@"
UPDATE [UserLessonProgress]
SET [BestPercent] =
    CASE
        WHEN [CompletionState] = 2 OR [FirstAttemptWasPerfect] = 1 THEN 100
        WHEN [CompletionState] = 1 AND [Percent] < 50 THEN 50
        ELSE [Percent]
    END;");

            migrationBuilder.CreateTable(
                name: "ProgressRequestLogs",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgressRequestLogs", x => new { x.UserId, x.RequestId });
                    table.ForeignKey(
                        name: "FK_ProgressRequestLogs_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProgressRequestLog_Retention",
                table: "ProgressRequestLogs",
                column: "CreatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProgressRequestLogs");

            migrationBuilder.DropColumn(
                name: "BestPercent",
                table: "UserLessonProgress");
        }
    }
}
