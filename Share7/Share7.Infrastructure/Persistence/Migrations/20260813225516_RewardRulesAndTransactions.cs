using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RewardRulesAndTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RewardRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    ReferenceKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RepeatPolicy = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CooldownSeconds = table.Column<int>(type: "int", nullable: true),
                    DailyLimit = table.Column<int>(type: "int", nullable: true),
                    TransactionType = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RewardRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RewardRuleGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RewardRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RewardRuleGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RewardRuleGrants_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RewardRuleGrants_RewardRules_RewardRuleId",
                        column: x => x.RewardRuleId,
                        principalTable: "RewardRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RewardTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RewardRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SubmissionKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RewardTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RewardTransactions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RewardTransactions_RewardRules_RewardRuleId",
                        column: x => x.RewardRuleId,
                        principalTable: "RewardRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RewardTransactionLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RewardTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    BalanceAfter = table.Column<long>(type: "bigint", nullable: false),
                    LedgerEntryId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RewardTransactionLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RewardTransactionLines_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RewardTransactionLines_RewardTransactions_RewardTransactionId",
                        column: x => x.RewardTransactionId,
                        principalTable: "RewardTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RewardRuleGrants_CurrencyId",
                table: "RewardRuleGrants",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_RewardRuleGrants_RewardRuleId_CurrencyId",
                table: "RewardRuleGrants",
                columns: new[] { "RewardRuleId", "CurrencyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RewardRules_EventType_Enabled_ReferenceKey",
                table: "RewardRules",
                columns: new[] { "EventType", "Enabled", "ReferenceKey" });

            migrationBuilder.CreateIndex(
                name: "IX_RewardTransactionLines_CurrencyId",
                table: "RewardTransactionLines",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_RewardTransactionLines_RewardTransactionId",
                table: "RewardTransactionLines",
                column: "RewardTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_RewardTransactions_RewardRuleId",
                table: "RewardTransactions",
                column: "RewardRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_RewardTransactions_UserId_RewardRuleId_CreatedAtUtc",
                table: "RewardTransactions",
                columns: new[] { "UserId", "RewardRuleId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RewardTransactions_UserId_RewardRuleId_IdempotencyKey",
                table: "RewardTransactions",
                columns: new[] { "UserId", "RewardRuleId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RewardRuleGrants");

            migrationBuilder.DropTable(
                name: "RewardTransactionLines");

            migrationBuilder.DropTable(
                name: "RewardTransactions");

            migrationBuilder.DropTable(
                name: "RewardRules");
        }
    }
}
