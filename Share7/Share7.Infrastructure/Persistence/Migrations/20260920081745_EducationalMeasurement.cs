using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EducationalMeasurement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemStatistics",
                columns: table => new
                {
                    ItemVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Population = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NTotal = table.Column<int>(type: "int", nullable: false),
                    NCorrect = table.Column<int>(type: "int", nullable: false),
                    NFirstEncounter = table.Column<int>(type: "int", nullable: false),
                    NFirstEncounterCorrect = table.Column<int>(type: "int", nullable: false),
                    SumElapsedMs = table.Column<long>(type: "bigint", nullable: false),
                    NElapsed = table.Column<int>(type: "int", nullable: false),
                    DiscriminationNumerator = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    DiscriminationDenominator = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ChoiceFrequency = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastObservationSequence = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemStatistics", x => new { x.ItemVersionId, x.Population });
                    table.ForeignKey(
                        name: "FK_ItemStatistics_ItemVersions_ItemVersionId",
                        column: x => x.ItemVersionId,
                        principalTable: "ItemVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MasteryRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    MinObservations = table.Column<int>(type: "int", nullable: false),
                    MinStrength = table.Column<int>(type: "int", nullable: false),
                    MasteredIntervalLowAtLeast = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    DevelopingEstimateAtLeast = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RetiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasteryRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Measurements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LearnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MethodKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Estimate = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    IntervalLow = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    IntervalHigh = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    ObservationCount = table.Column<int>(type: "int", nullable: false),
                    AssessmentCount = table.Column<int>(type: "int", nullable: false),
                    CorrectCount = table.Column<int>(type: "int", nullable: false),
                    LastObservationSequence = table.Column<long>(type: "bigint", nullable: false),
                    ComputedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Measurements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Measurements_LearningTargets_TargetId",
                        column: x => x.TargetId,
                        principalTable: "LearningTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Observations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LearnerResponseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LearnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    Strength = table.Column<int>(type: "int", nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    EvidenceContractVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ObservedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExcludedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExclusionReason = table.Column<int>(type: "int", nullable: true),
                    ExcludedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExclusionNote = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Observations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Observations_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Observations_LearnerResponses_LearnerResponseId",
                        column: x => x.LearnerResponseId,
                        principalTable: "LearnerResponses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Observations_LearningTargets_TargetId",
                        column: x => x.TargetId,
                        principalTable: "LearningTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MasteryVerdicts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LearnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MasteryRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MeasurementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    AdmittedObservations = table.Column<int>(type: "int", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasteryVerdicts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MasteryVerdicts_LearningTargets_TargetId",
                        column: x => x.TargetId,
                        principalTable: "LearningTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MasteryVerdicts_MasteryRules_MasteryRuleId",
                        column: x => x.MasteryRuleId,
                        principalTable: "MasteryRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MasteryVerdicts_Measurements_MeasurementId",
                        column: x => x.MeasurementId,
                        principalTable: "Measurements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "MasteryRules",
                columns: new[] { "Id", "CreatedAtUtc", "Description", "DevelopingEstimateAtLeast", "MasteredIntervalLowAtLeast", "MinObservations", "MinStrength", "PublishedAtUtc", "RetiredAtUtc", "RuleKey", "VersionNumber" },
                values: new object[] { new Guid("c9f5a7b1-0e82-4d36-94c2-1a6e3d50a799"), new DateTime(2026, 9, 20, 0, 0, 0, 0, DateTimeKind.Utc), "Mastered when at least 8 admitted observations put the lower bound of the 95% Wilson interval at or above 0.80. Developing when the point estimate is at or above 0.50. Insufficient below 8 observations, which is a stated absence of evidence rather than a low score.", 0.50m, 0.80m, 8, 2, new DateTime(2026, 9, 20, 0, 0, 0, 0, DateTimeKind.Utc), null, "platform.default", 1 });

            migrationBuilder.CreateIndex(
                name: "IX_ItemStatistics_ItemId",
                table: "ItemStatistics",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MasteryRules_RuleKey_VersionNumber",
                table: "MasteryRules",
                columns: new[] { "RuleKey", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MasteryVerdicts_LearnerId_TargetId",
                table: "MasteryVerdicts",
                columns: new[] { "LearnerId", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MasteryVerdicts_MasteryRuleId",
                table: "MasteryVerdicts",
                column: "MasteryRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_MasteryVerdicts_MeasurementId",
                table: "MasteryVerdicts",
                column: "MeasurementId");

            migrationBuilder.CreateIndex(
                name: "IX_MasteryVerdicts_TargetId",
                table: "MasteryVerdicts",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "IX_Measurements_LearnerId_TargetId_MethodKey",
                table: "Measurements",
                columns: new[] { "LearnerId", "TargetId", "MethodKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Measurements_TargetId",
                table: "Measurements",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "IX_Observations_ItemId",
                table: "Observations",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Observations_ItemVersionId",
                table: "Observations",
                column: "ItemVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Observations_LearnerId_TargetId_ExcludedAtUtc",
                table: "Observations",
                columns: new[] { "LearnerId", "TargetId", "ExcludedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Observations_Response_Target",
                table: "Observations",
                columns: new[] { "LearnerResponseId", "TargetId" },
                unique: true,
                filter: "[LearnerResponseId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Observations_Sequence",
                table: "Observations",
                column: "Sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Observations_TargetId",
                table: "Observations",
                column: "TargetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemStatistics");

            migrationBuilder.DropTable(
                name: "MasteryVerdicts");

            migrationBuilder.DropTable(
                name: "Observations");

            migrationBuilder.DropTable(
                name: "MasteryRules");

            migrationBuilder.DropTable(
                name: "Measurements");
        }
    }
}
