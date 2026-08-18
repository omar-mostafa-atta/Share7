using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OffersAndPurchaseTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Offers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Price = table.Column<long>(type: "bigint", nullable: false),
                    OriginalPrice = table.Column<long>(type: "bigint", nullable: true),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Availability = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PurchaseLimit = table.Column<int>(type: "int", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    BadgeKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Offers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Offers_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfferProducts",
                columns: table => new
                {
                    OfferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfferProducts", x => new { x.OfferId, x.ProductId });
                    table.ForeignKey(
                        name: "FK_OfferProducts_Offers_OfferId",
                        column: x => x.OfferId,
                        principalTable: "Offers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OfferProducts_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfferTranslations",
                columns: table => new
                {
                    OfferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Lang_Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfferTranslations", x => new { x.OfferId, x.Lang_Id });
                    table.ForeignKey(
                        name: "FK_OfferTranslations_Languages_Lang_Id",
                        column: x => x.Lang_Id,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfferTranslations_Offers_OfferId",
                        column: x => x.OfferId,
                        principalTable: "Offers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OfferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Price = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FailureReasonKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseTransactions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseTransactions_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseTransactions_Offers_OfferId",
                        column: x => x.OfferId,
                        principalTable: "Offers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OfferProducts_ProductId",
                table: "OfferProducts",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Offers_CurrencyId",
                table: "Offers",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Offers_SortOrder_Id",
                table: "Offers",
                columns: new[] { "SortOrder", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_OfferTranslations_Lang_Id_Name",
                table: "OfferTranslations",
                columns: new[] { "Lang_Id", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseTransactions_CurrencyId",
                table: "PurchaseTransactions",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseTransactions_OfferId",
                table: "PurchaseTransactions",
                column: "OfferId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseTransactions_UserId_OfferId_State",
                table: "PurchaseTransactions",
                columns: new[] { "UserId", "OfferId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseTransactions_UserId_RequestId",
                table: "PurchaseTransactions",
                columns: new[] { "UserId", "RequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OfferProducts");

            migrationBuilder.DropTable(
                name: "OfferTranslations");

            migrationBuilder.DropTable(
                name: "PurchaseTransactions");

            migrationBuilder.DropTable(
                name: "Offers");
        }
    }
}
