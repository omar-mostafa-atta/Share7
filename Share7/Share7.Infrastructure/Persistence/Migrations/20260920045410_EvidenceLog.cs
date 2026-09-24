using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EvidenceLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EvidenceContracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InteractionKind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceContracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvidenceContracts_GameModes_ModeId",
                        column: x => x.ModeId,
                        principalTable: "GameModes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EvidenceContracts_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EvidenceContractVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    StrengthWhenControlled = table.Column<int>(type: "int", nullable: false),
                    StrengthOtherwise = table.Column<int>(type: "int", nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    RequiresFirstEncounter = table.Column<bool>(type: "bit", nullable: false),
                    RequiresUnhinted = table.Column<bool>(type: "bit", nullable: false),
                    RequiresNoRetry = table.Column<bool>(type: "bit", nullable: false),
                    AdmittedContexts = table.Column<int>(type: "int", nullable: false),
                    IsIndividuallyAttributable = table.Column<bool>(type: "bit", nullable: false),
                    Justification = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RetiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceContractVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvidenceContractVersions_EvidenceContracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "EvidenceContracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LearnerResponses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LearnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LangId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false),
                    WasUnrecognised = table.Column<bool>(type: "bit", nullable: false),
                    AttemptOrdinal = table.Column<int>(type: "int", nullable: false),
                    IsFirstEncounter = table.Column<bool>(type: "bit", nullable: false),
                    HintsUsed = table.Column<int>(type: "int", nullable: false),
                    ElapsedMs = table.Column<int>(type: "int", nullable: true),
                    TimeLimitMs = table.Column<int>(type: "int", nullable: true),
                    RetryPermitted = table.Column<bool>(type: "bit", nullable: false),
                    WasAided = table.Column<bool>(type: "bit", nullable: false),
                    DeliveryMode = table.Column<int>(type: "int", nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EvidenceContractVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PlayContext = table.Column<int>(type: "int", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContentVersion = table.Column<int>(type: "int", nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearnerResponses_EvidenceContractVersions_EvidenceContractVersionId",
                        column: x => x.EvidenceContractVersionId,
                        principalTable: "EvidenceContractVersions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LearnerResponses_Questions_ItemVersionId",
                        column: x => x.ItemVersionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "EvidenceContracts",
                columns: new[] { "Id", "ContractKey", "CreatedAtUtc", "Description", "GameId", "InteractionKind", "ModeId" },
                values: new object[] { new Guid("b1e7c4a9-2f68-4d3b-9e57-0a4c8d15f2b6"), "platform.lesson_attempt", new DateTime(2026, 9, 20, 0, 0, 0, 0, DateTimeKind.Utc), "A lesson attempt posted to POST /api/progress/attempts is an administration of assessment items. Applies to every game and mode unless a more specific contract exists.", null, "item_response", null });

            migrationBuilder.InsertData(
                table: "EvidenceContractVersions",
                columns: new[] { "Id", "AdmittedContexts", "ContractId", "IsIndividuallyAttributable", "Justification", "PublishedAtUtc", "PublishedByUserId", "RequiresFirstEncounter", "RequiresNoRetry", "RequiresUnhinted", "RetiredAtUtc", "StrengthOtherwise", "StrengthWhenControlled", "VersionNumber", "Weight" },
                values: new object[] { new Guid("c2f8d5ba-3079-4e4c-af68-1b5d9e26a3c7"), 31, new Guid("b1e7c4a9-2f68-4d3b-9e57-0a4c8d15f2b6"), true, "The attempt endpoint accepts nothing but item responses, and grades them server-side against the stored answer key. Gameplay signals (distance, coins, combo, survival) arrive on the Run and telemetry paths, which have no contract and no route into this schema.", new DateTime(2026, 9, 20, 0, 0, 0, 0, DateTimeKind.Utc), null, true, true, true, null, 2, 3, 1, 1.0m });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceContracts_ContractKey",
                table: "EvidenceContracts",
                column: "ContractKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceContracts_GameId_ModeId_InteractionKind",
                table: "EvidenceContracts",
                columns: new[] { "GameId", "ModeId", "InteractionKind" },
                unique: true,
                filter: "[GameId] IS NOT NULL AND [ModeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceContracts_ModeId",
                table: "EvidenceContracts",
                column: "ModeId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceContractVersions_ContractId_VersionNumber",
                table: "EvidenceContractVersions",
                columns: new[] { "ContractId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerResponses_EvidenceContractVersionId",
                table: "LearnerResponses",
                column: "EvidenceContractVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerResponses_ItemVersionId",
                table: "LearnerResponses",
                column: "ItemVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerResponses_LearnerId_ItemVersionId_OccurredAtUtc",
                table: "LearnerResponses",
                columns: new[] { "LearnerId", "ItemVersionId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerResponses_LearnerId_NodeId",
                table: "LearnerResponses",
                columns: new[] { "LearnerId", "NodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerResponses_Sequence",
                table: "LearnerResponses",
                column: "Sequence",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LearnerResponses");

            migrationBuilder.DropTable(
                name: "EvidenceContractVersions");

            migrationBuilder.DropTable(
                name: "EvidenceContracts");
        }
    }
}
