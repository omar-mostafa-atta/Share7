using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds <c>Equipments</c> — the player's saved avatar outfit, **one row per equipped item**.
    /// A player wearing a helmet and a shield has two rows.
    /// <para>
    /// <c>IX_Equipments_UserId_SlotKey</c> is unique and deliberately **unfiltered**. EF's default
    /// for a unique index over a nullable column is to add <c>WHERE [SlotKey] IS NOT NULL</c>,
    /// which would exclude the no-items rows described below and let one account accumulate any
    /// number of them. Unfiltered, SQL Server treats nulls as equal, so a user gets at most one.
    /// </para>
    /// <para>
    /// <c>SlotKey</c>/<c>CosmeticKey</c> are nullable for exactly one reason: a player who takes
    /// everything off keeps a single row with both null, recording that they have saved an empty
    /// outfit. Without it, "took everything off" and "has never saved" are both zero rows and the
    /// client cannot tell them apart — the distinction <c>UpdatedAtUtc</c> carries, and getting it
    /// wrong strips every undressed player on next launch. <c>ColorKey</c> is independently
    /// nullable: a cosmetic may be worn with no colour chosen.
    /// </para>
    /// <para>
    /// <c>BodyType</c> and <c>UpdatedAtUtc</c> are per player, so every row of one user's outfit
    /// repeats them. Written together on every save, so the copies cannot drift.
    /// </para>
    /// </summary>
    public partial class UserEquipment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Equipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BodyType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SlotKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CosmeticKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ColorKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Equipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Equipments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Equipments_UserId_SlotKey",
                table: "Equipments",
                columns: new[] { "UserId", "SlotKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Equipments");
        }
    }
}
