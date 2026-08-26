using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TelemetryPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TelemetryDailyMetrics",
                columns: table => new
                {
                    DayUtc = table.Column<DateTime>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Dimension = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DimensionValue = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Count = table.Column<long>(type: "bigint", nullable: false),
                    UniqueUsers = table.Column<int>(type: "int", nullable: true),
                    UniqueUsersComputedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryDailyMetrics", x => new { x.DayUtc, x.Name, x.Dimension, x.DimensionValue });
                });

            migrationBuilder.CreateTable(
                name: "TelemetryEvents",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DayUtc = table.Column<DateTime>(type: "date", nullable: false),
                    ClientSeq = table.Column<int>(type: "int", nullable: false),
                    AppVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DeviceModel = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Locale = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ParamsJson = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    SampleRate = table.Column<double>(type: "float", nullable: false),
                    IsUnregistered = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryEvents", x => x.Sequence);
                    table.ForeignKey(
                        name: "FK_TelemetryEvents_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryEventSchemas",
                columns: table => new
                {
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Group = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SampleRate = table.Column<double>(type: "float", nullable: false, defaultValue: 1.0),
                    RetentionDays = table.Column<int>(type: "int", nullable: true),
                    Enabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    RollUpDaily = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Dimensions = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryEventSchemas", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryRetentionCohorts",
                columns: table => new
                {
                    CohortDayUtc = table.Column<DateTime>(type: "date", nullable: false),
                    DayIndex = table.Column<int>(type: "int", nullable: false),
                    CohortSize = table.Column<int>(type: "int", nullable: false),
                    RetainedUsers = table.Column<int>(type: "int", nullable: false),
                    ComputedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryRetentionCohorts", x => new { x.CohortDayUtc, x.DayIndex });
                });

            migrationBuilder.CreateTable(
                name: "TelemetrySessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DurationSeconds = table.Column<int>(type: "int", nullable: false),
                    EventCount = table.Column<int>(type: "int", nullable: false),
                    AppVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DayUtc = table.Column<DateTime>(type: "date", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetrySessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelemetrySessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryUserDays",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayUtc = table.Column<DateTime>(type: "date", nullable: false),
                    FirstSeenDayUtc = table.Column<DateTime>(type: "date", nullable: false),
                    DayIndex = table.Column<int>(type: "int", nullable: false),
                    SessionCount = table.Column<int>(type: "int", nullable: false),
                    EventCount = table.Column<int>(type: "int", nullable: false),
                    PlaySeconds = table.Column<int>(type: "int", nullable: false),
                    RunCount = table.Column<int>(type: "int", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryUserDays", x => new { x.UserId, x.DayUtc });
                    table.ForeignKey(
                        name: "FK_TelemetryUserDays_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryUserLifecycle",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CohortDayUtc = table.Column<DateTime>(type: "date", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActiveDays = table.Column<int>(type: "int", nullable: false),
                    TotalSessions = table.Column<int>(type: "int", nullable: false),
                    TotalEvents = table.Column<long>(type: "bigint", nullable: false),
                    TotalPlaySeconds = table.Column<long>(type: "bigint", nullable: false),
                    InstallAppVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    InstallPlatform = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    LastAppVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    LastPlatform = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryUserLifecycle", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_TelemetryUserLifecycle_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryMetric_Name",
                table: "TelemetryDailyMetrics",
                columns: new[] { "Name", "DayUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryEvent_Day",
                table: "TelemetryEvents",
                columns: new[] { "DayUtc", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryEvent_Session",
                table: "TelemetryEvents",
                columns: new[] { "SessionId", "ClientSeq" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryEvent_User",
                table: "TelemetryEvents",
                columns: new[] { "UserId", "ReceivedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UX_TelemetryEvent_Idem",
                table: "TelemetryEvents",
                columns: new[] { "UserId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelemetrySchema_Unregistered",
                table: "TelemetryEventSchemas",
                column: "FirstSeenAtUtc",
                filter: "[FirstSeenAtUtc] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryCohort_DayIndex",
                table: "TelemetryRetentionCohorts",
                column: "DayIndex");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetrySession_Day",
                table: "TelemetrySessions",
                column: "DayUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetrySession_User",
                table: "TelemetrySessions",
                columns: new[] { "UserId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryUserDay_Cohort",
                table: "TelemetryUserDays",
                columns: new[] { "FirstSeenDayUtc", "DayIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryUserDay_Day",
                table: "TelemetryUserDays",
                column: "DayUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryLifecycle_Cohort",
                table: "TelemetryUserLifecycle",
                column: "CohortDayUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryLifecycle_LastSeen",
                table: "TelemetryUserLifecycle",
                column: "LastSeenAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TelemetryDailyMetrics");

            migrationBuilder.DropTable(
                name: "TelemetryEvents");

            migrationBuilder.DropTable(
                name: "TelemetryEventSchemas");

            migrationBuilder.DropTable(
                name: "TelemetryRetentionCohorts");

            migrationBuilder.DropTable(
                name: "TelemetrySessions");

            migrationBuilder.DropTable(
                name: "TelemetryUserDays");

            migrationBuilder.DropTable(
                name: "TelemetryUserLifecycle");
        }
    }
}
