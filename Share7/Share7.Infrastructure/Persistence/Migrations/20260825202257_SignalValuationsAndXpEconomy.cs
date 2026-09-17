using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SignalValuationsAndXpEconomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // **A rename, not a drop and recreate.** EF scaffolds the destructive form because it
            // compares two models rather than reading intent, and taking it literally here would
            // delete the live economy: every price for every kind in every game, replaced with an
            // empty table that pays nothing until somebody notices and re-authors the lot.
            //
            // The indexes are dropped and rebuilt around the column rename because SQL Server will
            // not rename a column that an index is defined over.
            migrationBuilder.DropIndex(name: "UX_Valuation", table: "PickupValuations");
            migrationBuilder.DropIndex(name: "IX_Valuation_Kind", table: "PickupValuations");

            migrationBuilder.RenameTable(name: "PickupValuations", newName: "SignalValuations");

            migrationBuilder.RenameColumn(
                name: "PickupKind", table: "SignalValuations", newName: "SignalKind");

            migrationBuilder.RenameIndex(
                name: "IX_PickupValuations_CurrencyId",
                table: "SignalValuations",
                newName: "IX_SignalValuations_CurrencyId");

            // Per-kind rate bound. Null means "use the platform default", which is what every
            // existing row wants — the bound they have been settling under has not changed.
            migrationBuilder.AddColumn<double>(
                name: "MaxPerSecond",
                table: "SignalValuations",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BestCorrectCount",
                table: "UserLessonProgress",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "DailySignalLedger",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignalKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DayUtc = table.Column<DateTime>(type: "date", nullable: false),
                    PaidCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailySignalLedger", x => new { x.UserId, x.SignalKind, x.DayUtc });
                    table.ForeignKey(
                        name: "FK_DailySignalLedger_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "LevelThresholds",
                columns: new[] { "Level", "CreatedAtUtc", "CumulativeXp", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { 51, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 63750L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 52, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 66300L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 53, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 68900L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 54, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 71550L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 55, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 74250L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 56, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 77000L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 57, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 79800L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 58, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 82650L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 59, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 85550L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 60, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 88500L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 61, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 91500L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 62, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 94550L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 63, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 97650L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 64, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 100800L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 65, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 104000L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 66, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 107250L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 67, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 110550L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 68, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 113900L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 69, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 117300L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 70, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 120750L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 71, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 124250L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 72, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 127800L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 73, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 131400L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 74, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 135050L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 75, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 138750L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 76, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 142500L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 77, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 146300L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 78, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 150150L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 79, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 154050L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 80, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 158000L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 81, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 162000L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 82, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 166050L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 83, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 170150L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 84, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 174300L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 85, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 178500L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 86, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 182750L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 87, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 187050L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 88, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 191400L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 89, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 195800L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 90, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 200250L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 91, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 204750L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 92, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 209300L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 93, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 213900L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 94, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 218550L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 95, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 223250L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 96, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 228000L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 97, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 232800L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 98, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 237650L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 99, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 242550L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 100, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 247500L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Valuation_Kind",
                table: "SignalValuations",
                columns: new[] { "SignalKind", "Enabled" });

            // filter: null is load-bearing rather than noise: without it SQL Server would scope the
            // unique index to rows with a non-null GameId, leaving the platform-default rows — the
            // ones every unconfigured mini-game resolves through — entirely unconstrained.
            migrationBuilder.CreateIndex(
                name: "UX_Valuation",
                table: "SignalValuations",
                columns: new[] { "GameId", "SignalKind", "CurrencyId" },
                unique: true,
                filter: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailySignalLedger");

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 51);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 52);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 53);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 54);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 55);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 56);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 57);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 58);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 59);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 60);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 61);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 62);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 63);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 64);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 65);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 66);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 67);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 68);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 69);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 70);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 71);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 72);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 73);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 74);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 75);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 76);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 77);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 78);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 79);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 80);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 81);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 82);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 83);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 84);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 85);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 86);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 87);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 88);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 89);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 90);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 91);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 92);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 93);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 94);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 95);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 96);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 97);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 98);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 99);

            migrationBuilder.DeleteData(
                table: "LevelThresholds",
                keyColumn: "Level",
                keyValue: 100);

            migrationBuilder.DropColumn(
                name: "BestCorrectCount",
                table: "UserLessonProgress");

            // The mirror of Up: rename back, keeping every row. Dropping and recreating here would
            // make a rollback destroy the economy just as thoroughly as the scaffolded Up would have.
            migrationBuilder.DropIndex(name: "UX_Valuation", table: "SignalValuations");
            migrationBuilder.DropIndex(name: "IX_Valuation_Kind", table: "SignalValuations");

            migrationBuilder.DropColumn(name: "MaxPerSecond", table: "SignalValuations");

            migrationBuilder.RenameColumn(
                name: "SignalKind", table: "SignalValuations", newName: "PickupKind");

            migrationBuilder.RenameIndex(
                name: "IX_SignalValuations_CurrencyId",
                table: "SignalValuations",
                newName: "IX_PickupValuations_CurrencyId");

            migrationBuilder.RenameTable(name: "SignalValuations", newName: "PickupValuations");

            migrationBuilder.CreateIndex(
                name: "IX_Valuation_Kind",
                table: "PickupValuations",
                columns: new[] { "PickupKind", "Enabled" });

            migrationBuilder.CreateIndex(
                name: "UX_Valuation",
                table: "PickupValuations",
                columns: new[] { "GameId", "PickupKind", "CurrencyId" },
                unique: true,
                filter: null);
        }
    }
}
