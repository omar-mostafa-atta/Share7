using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Moves grant kind out of an enum column on <c>ProductGrants</c> and up onto <c>Products</c> as
    /// a foreign key to a new admin-managed <c>ProductKinds</c> table, and gives products the
    /// description and art the shop needs.
    /// <para>
    /// **Hand-edited after scaffolding, and the order matters.** EF generated the drops first, which
    /// would have destroyed <c>ProductGrants.Kind</c> before the backfill could read it and left
    /// every product pointing at an all-zero GUID that the new foreign key would then reject. This
    /// runs unattended via <c>MigrateAsync()</c> on startup — including against the MonsterASP.NET
    /// database, which cannot be reached from a dev machine — so a failure here takes the app down
    /// rather than failing a command someone is watching.
    /// </para>
    /// <para>
    /// **This drops columns.** <c>Products.Metadata</c>, <c>CreatedAtUtc</c> and <c>UpdatedAtUtc</c>
    /// go, per the reshaped contract; whatever was in them is not recoverable from here.
    /// </para>
    /// </summary>
    public partial class ProductKindsAndProductReshape : Migration
    {
        /// <summary>
        /// Seeded so the vocabulary the client already speaks survives the move — every existing
        /// grant was one of these two. Fixed rather than generated so both environments agree, but
        /// **not** constants in code: kinds are admin-managed rows now, and either of these can be
        /// renamed or deleted once nothing uses it.
        /// </summary>
        private const string CosmeticKindId = "7f3a5c81-2d64-4e90-b1a7-3c58e02f9d46";
        private const string ContentPackKindId = "5e2b9d47-8c13-4a76-9f02-6b41d7c3a85e";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- 1. the new lookup, populated with the vocabulary being replaced ----------------

            migrationBuilder.CreateTable(
                name: "ProductKinds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductKinds", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductKinds_Name",
                table: "ProductKinds",
                column: "Name",
                unique: true);

            migrationBuilder.Sql($"""
                INSERT INTO [ProductKinds] ([Id], [Name], [Description])
                VALUES
                    ('{CosmeticKindId}', 'Cosmetic',
                     'Client-side art — skins, trails, avatars. The reference is the cosmetic id.'),
                    ('{ContentPackKindId}', 'Content Pack',
                     'A pack delivered by Unity Addressables/CDN. The reference is the packId the content manifest reports.');
                """);

            // ---- 2. the new product columns ----------------------------------------------------

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Products",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Products",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            // Nullable for now. Adding it non-nullable would need a default, and a lingering DEFAULT
            // constraint on a foreign key shows up as drift in the next migration — so it is
            // backfilled and then tightened below instead.
            migrationBuilder.AddColumn<Guid>(
                name: "ProductKindId",
                table: "Products",
                type: "uniqueidentifier",
                nullable: true);

            // ---- 3. backfill, while ProductGrants.Kind still exists -----------------------------

            // Kind was per grant and is per product now, so a product whose grants disagreed has to
            // collapse to one: the kind most of its grants used, ties broken alphabetically for a
            // deterministic result. Nothing is lost that the admin cannot re-categorise, and
            // re-categorising an owned product stays allowed precisely because of cases like this.
            migrationBuilder.Sql($"""
                WITH Ranked AS (
                    SELECT
                        [ProductId],
                        [Kind],
                        ROW_NUMBER() OVER (
                            PARTITION BY [ProductId]
                            ORDER BY COUNT(*) DESC, [Kind]
                        ) AS Ordinal
                    FROM [ProductGrants]
                    GROUP BY [ProductId], [Kind]
                )
                UPDATE p
                SET p.[ProductKindId] = CASE WHEN r.[Kind] = 'CONTENT_PACK'
                                             THEN '{ContentPackKindId}'
                                             ELSE '{CosmeticKindId}'
                                        END
                FROM [Products] p
                INNER JOIN Ranked r ON r.[ProductId] = p.[Id] AND r.Ordinal = 1;
                """);

            // Products with no grants at all — legal now that grants are authored separately, and
            // possible before this if a grant was deleted directly in SQL.
            migrationBuilder.Sql($"""
                UPDATE [Products]
                SET [ProductKindId] = '{CosmeticKindId}'
                WHERE [ProductKindId] IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductKindId",
                table: "Products",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            // ---- 4. retire the old grant kind --------------------------------------------------

            migrationBuilder.DropIndex(
                name: "IX_ProductGrants_ProductId_Kind_Reference",
                table: "ProductGrants");

            // (ProductId, Reference) was only unique *per kind* before, so one product granting the
            // same reference as both a COSMETIC and a CONTENT_PACK was legal and would now break the
            // narrower index — mid-startup, on a database nobody can reach. Keep the first row.
            migrationBuilder.Sql("""
                WITH Duplicates AS (
                    SELECT
                        [Id],
                        ROW_NUMBER() OVER (
                            PARTITION BY [ProductId], [Reference]
                            ORDER BY [Kind], [Id]
                        ) AS Ordinal
                    FROM [ProductGrants]
                )
                DELETE FROM [ProductGrants]
                WHERE [Id] IN (SELECT [Id] FROM Duplicates WHERE Ordinal > 1);
                """);

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ProductGrants");

            migrationBuilder.CreateIndex(
                name: "IX_ProductGrants_ProductId_Reference",
                table: "ProductGrants",
                columns: new[] { "ProductId", "Reference" },
                unique: true);

            // ---- 5. drop what the reshaped product no longer carries ---------------------------

            migrationBuilder.DropColumn(
                name: "Metadata",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "Products");

            // ---- 6. wire the foreign key, now that every row points somewhere real --------------

            migrationBuilder.CreateIndex(
                name: "IX_Products_ProductKindId",
                table: "Products",
                column: "ProductKindId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_ProductKinds_ProductKindId",
                table: "Products",
                column: "ProductKindId",
                principalTable: "ProductKinds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_ProductKinds_ProductKindId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_ProductKindId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_ProductGrants_ProductId_Reference",
                table: "ProductGrants");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "ProductGrants",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // Best effort back: kind is recoverable per product from the foreign key, which is more
            // than the dropped columns below manage. Metadata and the timestamps come back empty —
            // going down is a schema rollback, not a restore.
            migrationBuilder.Sql($"""
                UPDATE g
                SET g.[Kind] = CASE WHEN p.[ProductKindId] = '{ContentPackKindId}'
                                    THEN 'CONTENT_PACK'
                                    ELSE 'COSMETIC'
                               END
                FROM [ProductGrants] g
                INNER JOIN [Products] p ON p.[Id] = g.[ProductId];
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ProductGrants_ProductId_Kind_Reference",
                table: "ProductGrants",
                columns: new[] { "ProductId", "Kind", "Reference" },
                unique: true);

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ProductKindId",
                table: "Products");

            migrationBuilder.DropTable(
                name: "ProductKinds");

            migrationBuilder.AddColumn<string>(
                name: "Metadata",
                table: "Products",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Products",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "Products",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }
    }
}
