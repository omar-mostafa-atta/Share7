using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CurrencyKeysAndLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Currencies_Name",
                table: "Currencies");

            migrationBuilder.AddColumn<bool>(
                name: "Enabled",
                table: "Currencies",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Key",
                table: "Currencies",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // Every existing row just got Key = '' from the default above, so the unique index
            // below would fail the moment there is more than one currency — and this migration
            // runs unattended on startup, which would take the whole app down rather than fail a
            // command someone is watching. Derive a key from the name first.
            //
            // Names are only unique case-insensitively, and folding them can collapse two rows
            // onto one key, so genuine collisions get a numeric suffix rather than breaking the
            // index.
            migrationBuilder.Sql("""
                WITH Keyed AS (
                    SELECT
                        [Id],
                        LOWER(REPLACE(LTRIM(RTRIM([Name])), ' ', '_')) AS BaseKey,
                        ROW_NUMBER() OVER (
                            PARTITION BY LOWER(REPLACE(LTRIM(RTRIM([Name])), ' ', '_'))
                            ORDER BY [CreatedAtUtc], [Id]
                        ) AS Ordinal
                    FROM [Currencies]
                    WHERE [Key] = ''
                )
                UPDATE c
                SET c.[Key] = CASE WHEN k.Ordinal = 1
                                   THEN k.BaseKey
                                   ELSE k.BaseKey + '_' + CAST(k.Ordinal AS nvarchar(10))
                              END
                FROM [Currencies] c
                INNER JOIN Keyed k ON k.[Id] = c.[Id];
                """);

            migrationBuilder.CreateTable(
                name: "CurrencyLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    BalanceAfter = table.Column<long>(type: "bigint", nullable: false),
                    TransactionType = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Metadata = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrencyLedgerEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CurrencyLedgerEntries_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CurrencyLedgerEntries_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_Key",
                table: "Currencies",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyLedgerEntries_CurrencyId",
                table: "CurrencyLedgerEntries",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyLedgerEntries_IdempotencyKey",
                table: "CurrencyLedgerEntries",
                column: "IdempotencyKey");

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyLedgerEntries_UserId_CurrencyId_Id",
                table: "CurrencyLedgerEntries",
                columns: new[] { "UserId", "CurrencyId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CurrencyLedgerEntries");

            migrationBuilder.DropIndex(
                name: "IX_Currencies_Key",
                table: "Currencies");

            migrationBuilder.DropColumn(
                name: "Enabled",
                table: "Currencies");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "Currencies");

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_Name",
                table: "Currencies",
                column: "Name",
                unique: true);
        }
    }
}
