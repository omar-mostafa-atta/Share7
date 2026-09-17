using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RewardEntitlementGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RewardRuleEntitlementGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RewardRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RewardRuleEntitlementGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RewardRuleEntitlementGrants_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RewardRuleEntitlementGrants_RewardRules_RewardRuleId",
                        column: x => x.RewardRuleId,
                        principalTable: "RewardRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RewardRuleEntitlementGrants_ProductId",
                table: "RewardRuleEntitlementGrants",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_RewardRuleEntitlementGrants_RewardRuleId_ProductId",
                table: "RewardRuleEntitlementGrants",
                columns: new[] { "RewardRuleId", "ProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RewardRuleEntitlementGrants");
        }
    }
}
