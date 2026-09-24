using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorRoles = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Area = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TargetId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvent_Actor",
                table: "AuditEvents",
                columns: new[] { "ActorUserId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvent_Area",
                table: "AuditEvents",
                columns: new[] { "Area", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvent_Target",
                table: "AuditEvents",
                columns: new[] { "TargetType", "TargetId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvent_Time",
                table: "AuditEvents",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Sequence",
                table: "AuditEvents",
                column: "Sequence",
                unique: true);

            // **Append-only, enforced by the database rather than by convention.** No code path
            // updates or deletes an audit row, and this makes sure none ever can — including a
            // hand-written query against production. Archiving old rows under a retention policy is
            // a deliberate operation: disable the trigger in the same transaction as the archive.
            //
            // Declared to EF too (AuditEventConfiguration.AppendOnlyTrigger), which is what stops
            // EF using an OUTPUT clause SQL Server would refuse on a table with a trigger.
            migrationBuilder.Sql("""
                CREATE TRIGGER [dbo].[TR_AuditEvents_AppendOnly]
                ON [dbo].[AuditEvents]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, 'AuditEvents is append-only: audit rows cannot be updated or deleted.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [dbo].[TR_AuditEvents_AppendOnly];");

            migrationBuilder.DropTable(
                name: "AuditEvents");
        }
    }
}
