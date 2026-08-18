using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Moves shop text out of <c>Products</c> and <c>ProductKinds</c> into per-language child
    /// tables, following the curriculum tree: one row per (entity, language), and the parent row
    /// carries no display name at all.
    /// <para>
    /// <c>ProductKinds.Name</c> is the one thing that stays untranslated — it is normalised into the
    /// <c>kind</c> token every grant reports, and <c>COSMETIC</c> has to mean the same thing to an
    /// Arabic client as to an English one.
    /// </para>
    /// <para>
    /// **Hand-edited after scaffolding, and the order matters.** EF put the <c>DropColumn</c> calls
    /// first, which would have destroyed every existing name before the backfill could copy it. This
    /// runs unattended via <c>MigrateAsync()</c> on startup.
    /// </para>
    /// </summary>
    public partial class ProductAndKindTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- 1. the new tables -------------------------------------------------------------

            migrationBuilder.CreateTable(
                name: "ProductKindTranslations",
                columns: table => new
                {
                    ProductKindId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Lang_Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductKindTranslations", x => new { x.ProductKindId, x.Lang_Id });
                    table.ForeignKey(
                        name: "FK_ProductKindTranslations_Languages_Lang_Id",
                        column: x => x.Lang_Id,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductKindTranslations_ProductKinds_ProductKindId",
                        column: x => x.ProductKindId,
                        principalTable: "ProductKinds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductTranslations",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Lang_Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductTranslations", x => new { x.ProductId, x.Lang_Id });
                    table.ForeignKey(
                        name: "FK_ProductTranslations_Languages_Lang_Id",
                        column: x => x.Lang_Id,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductTranslations_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ---- 2. backfill, while the old columns still exist ---------------------------------

            // Cross joined against every configured language, so nothing lands half-translated and
            // the "a name for every language" rule holds for rows that predate it. Both languages
            // start with the same text; an admin retranslates from the console.
            migrationBuilder.Sql("""
                INSERT INTO [ProductTranslations] ([ProductId], [Lang_Id], [Name], [Description])
                SELECT p.[Id], l.[Id], p.[Name], p.[Description]
                FROM [Products] p
                CROSS JOIN [Languages] l;
                """);

            migrationBuilder.Sql("""
                INSERT INTO [ProductKindTranslations] ([ProductKindId], [Lang_Id], [Name], [Description])
                SELECT k.[Id], l.[Id], k.[Name], k.[Description]
                FROM [ProductKinds] k
                CROSS JOIN [Languages] l;
                """);

            // ---- 3. drop the text the parent rows no longer carry -------------------------------

            migrationBuilder.DropColumn(
                name: "Name",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "ProductKinds");

            migrationBuilder.CreateIndex(
                name: "IX_ProductKindTranslations_Lang_Id_Name",
                table: "ProductKindTranslations",
                columns: new[] { "Lang_Id", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductTranslations_Lang_Id_Name",
                table: "ProductTranslations",
                columns: new[] { "Lang_Id", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Products",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Products",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ProductKinds",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            // Collapse back to one language's text — English where it exists, otherwise whichever
            // row sorts first. Going down loses the other translations; it is a schema rollback,
            // not a restore.
            migrationBuilder.Sql("""
                WITH Preferred AS (
                    SELECT
                        t.[ProductId], t.[Name], t.[Description],
                        ROW_NUMBER() OVER (
                            PARTITION BY t.[ProductId]
                            ORDER BY CASE WHEN l.[Code] = 'en' THEN 0 ELSE 1 END, l.[Code]
                        ) AS Ordinal
                    FROM [ProductTranslations] t
                    INNER JOIN [Languages] l ON l.[Id] = t.[Lang_Id]
                )
                UPDATE p
                SET p.[Name] = x.[Name], p.[Description] = x.[Description]
                FROM [Products] p
                INNER JOIN Preferred x ON x.[ProductId] = p.[Id] AND x.Ordinal = 1;
                """);

            migrationBuilder.Sql("""
                WITH Preferred AS (
                    SELECT
                        t.[ProductKindId], t.[Description],
                        ROW_NUMBER() OVER (
                            PARTITION BY t.[ProductKindId]
                            ORDER BY CASE WHEN l.[Code] = 'en' THEN 0 ELSE 1 END, l.[Code]
                        ) AS Ordinal
                    FROM [ProductKindTranslations] t
                    INNER JOIN [Languages] l ON l.[Id] = t.[Lang_Id]
                )
                UPDATE k
                SET k.[Description] = x.[Description]
                FROM [ProductKinds] k
                INNER JOIN Preferred x ON x.[ProductKindId] = k.[Id] AND x.Ordinal = 1;
                """);

            migrationBuilder.DropTable(
                name: "ProductKindTranslations");

            migrationBuilder.DropTable(
                name: "ProductTranslations");
        }
    }
}
