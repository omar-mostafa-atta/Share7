using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RunsAndPickupValuations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyCurrencyLedger",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayUtc = table.Column<DateTime>(type: "date", nullable: false),
                    EarnedAmount = table.Column<long>(type: "bigint", nullable: false),
                    RunCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyCurrencyLedger", x => new { x.UserId, x.CurrencyId, x.DayUtc });
                    table.ForeignKey(
                        name: "FK_DailyCurrencyLedger_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DailyCurrencyLedger_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PickupValuations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PickupKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitValue = table.Column<long>(type: "bigint", nullable: false),
                    MaxPerRun = table.Column<int>(type: "int", nullable: false),
                    MaxPerDay = table.Column<int>(type: "int", nullable: true),
                    Enabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickupValuations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PickupValuations_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PickupValuations_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Seed = table.Column<long>(type: "bigint", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: false),
                    StartRequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ResultRequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    IsFlagged = table.Column<bool>(type: "bit", nullable: false),
                    FlagReason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CapReached = table.Column<bool>(type: "bit", nullable: false),
                    CapMessage = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PickupsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiersJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Runs_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Runs_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RunPayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CollectedCount = table.Column<int>(type: "int", nullable: false),
                    PaidCount = table.Column<int>(type: "int", nullable: false),
                    UnitValue = table.Column<long>(type: "bigint", nullable: false),
                    GrossAmount = table.Column<long>(type: "bigint", nullable: false),
                    CappedAmount = table.Column<long>(type: "bigint", nullable: false),
                    NetAmount = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunPayouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunPayouts_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RunPayouts_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyCurrencyLedger_CurrencyId",
                table: "DailyCurrencyLedger",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_PickupValuations_CurrencyId",
                table: "PickupValuations",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Valuation_Kind",
                table: "PickupValuations",
                columns: new[] { "PickupKind", "Enabled" });

            migrationBuilder.CreateIndex(
                name: "UX_Valuation",
                table: "PickupValuations",
                columns: new[] { "GameId", "PickupKind", "CurrencyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunPayouts_CurrencyId",
                table: "RunPayouts",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "UX_RunPayout_Line",
                table: "RunPayouts",
                columns: new[] { "RunId", "Source", "CurrencyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Run_Open",
                table: "Runs",
                columns: new[] { "State", "ExpiresAtUtc" },
                filter: "[State] = 'OPEN'");

            migrationBuilder.CreateIndex(
                name: "IX_Run_User",
                table: "Runs",
                columns: new[] { "UserId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Runs_GameId",
                table: "Runs",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "UX_Run_ResultIdem",
                table: "Runs",
                columns: new[] { "UserId", "ResultRequestId" },
                unique: true,
                filter: "[ResultRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Run_StartIdem",
                table: "Runs",
                columns: new[] { "UserId", "StartRequestId" },
                unique: true,
                filter: "[StartRequestId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyCurrencyLedger");

            migrationBuilder.DropTable(
                name: "PickupValuations");

            migrationBuilder.DropTable(
                name: "RunPayouts");

            migrationBuilder.DropTable(
                name: "Runs");
        }
    }
}
