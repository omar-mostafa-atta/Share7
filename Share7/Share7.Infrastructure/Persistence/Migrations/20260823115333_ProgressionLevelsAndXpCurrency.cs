using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProgressionLevelsAndXpCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // `IsHard` is deliberately NOT added here. RunBoundsReviewAndHardCurrency (102939) already
            // creates it, and this migration was generated against a model snapshot that had lost that
            // work — two sessions were writing the same snapshot file in parallel. Adding it twice is
            // what made a migrate-from-scratch fail with "Column name 'IsHard' ... specified more than
            // once", which took every test with it. The seed below still writes the column; it exists
            // by the time this runs.

            migrationBuilder.AddColumn<bool>(
                name: "IsSpendable",
                table: "Currencies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Backfill: every currency that already exists is spendable — coins above all. The
            // column default is deliberately left at the CLR default instead of being set to true,
            // because a store default here is what made EF drop `IsSpendable = false` from the xp
            // seed in the first place. An explicit UPDATE says what it means and leaves the model
            // and the database agreeing.
            migrationBuilder.Sql("UPDATE [Currencies] SET [IsSpendable] = 1;");

            migrationBuilder.CreateTable(
                name: "LevelThresholds",
                columns: table => new
                {
                    Level = table.Column<int>(type: "int", nullable: false),
                    CumulativeXp = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LevelThresholds", x => x.Level);
                });

            migrationBuilder.InsertData(
                table: "Currencies",
                columns: new[] { "Id", "CreatedAtUtc", "Description", "Enabled", "IsHard", "IsSpendable", "Key", "Name" },
                values: new object[] { new Guid("3f9c8b21-6d47-4e05-9a13-8c2e7f04b6d5"), new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), "Earned by playing and learning. Determines player level; cannot be spent.", true, false, false, "xp", "Experience" });

            migrationBuilder.InsertData(
                table: "LevelThresholds",
                columns: new[] { "Level", "CreatedAtUtc", "CumulativeXp", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 0L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 2, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 50L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 3, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 150L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 4, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 300L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 5, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 500L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 6, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 750L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 7, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 1050L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 8, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 1400L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 9, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 1800L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 10, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 2250L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 11, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 2750L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 12, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 3300L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 13, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 3900L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 14, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 4550L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 15, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 5250L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 16, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 6000L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 17, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 6800L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 18, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 7650L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 19, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 8550L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 20, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 9500L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 21, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 10500L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 22, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 11550L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 23, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 12650L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 24, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 13800L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 25, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 15000L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 26, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 16250L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 27, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 17550L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 28, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 18900L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 29, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 20300L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 30, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 21750L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 31, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 23250L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 32, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 24800L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 33, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 26400L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 34, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 28050L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 35, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 29750L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 36, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 31500L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 37, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 33300L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 38, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 35150L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 39, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 37050L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 40, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 39000L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 41, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 41000L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 42, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 43050L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 43, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 45150L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 44, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 47300L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 45, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 49500L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 46, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 51750L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 47, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 54050L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 48, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 56400L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 49, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 58800L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 50, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), 61250L, new DateTime(2026, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_LevelThresholds_CumulativeXp",
                table: "LevelThresholds",
                column: "CumulativeXp",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LevelThresholds");

            migrationBuilder.DeleteData(
                table: "Currencies",
                keyColumn: "Id",
                keyValue: new Guid("3f9c8b21-6d47-4e05-9a13-8c2e7f04b6d5"));

            // The matching DropColumn is gone too — the column belongs to the earlier migration, and
            // reverting this one must not take it away from the runs feature that still needs it.
            migrationBuilder.DropColumn(
                name: "IsSpendable",
                table: "Currencies");
        }
    }
}
