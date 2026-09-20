using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssessmentAndCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssessmentBlueprints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlueprintKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FrameworkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TotalItemCount = table.Column<int>(type: "int", nullable: true),
                    TimeLimitMs = table.Column<int>(type: "int", nullable: true),
                    RetryPermitted = table.Column<bool>(type: "bit", nullable: false),
                    RequiredStrength = table.Column<int>(type: "int", nullable: false),
                    MinCoverageRatio = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    MinAreaCoverageRatio = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    MinObservationsOverall = table.Column<int>(type: "int", nullable: false),
                    MinObservationsPerArea = table.Column<int>(type: "int", nullable: false),
                    MaxMedianEvidenceAgeDays = table.Column<int>(type: "int", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RetiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SourceNote = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentBlueprints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentBlueprints_CompetencyFrameworks_FrameworkId",
                        column: x => x.FrameworkId,
                        principalTable: "CompetencyFrameworks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamSpecifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SpecificationKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AuthorityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CountryCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    SubjectLabel = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RetiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamSpecifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamSpecifications_CurriculumAuthorities_AuthorityId",
                        column: x => x.AuthorityId,
                        principalTable: "CurriculumAuthorities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssessmentBlueprintAreas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlueprintId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AreaKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentBlueprintAreas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentBlueprintAreas_AssessmentBlueprints_BlueprintId",
                        column: x => x.BlueprintId,
                        principalTable: "AssessmentBlueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Assessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssessmentKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    BlueprintId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RetiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assessments_AssessmentBlueprints_BlueprintId",
                        column: x => x.BlueprintId,
                        principalTable: "AssessmentBlueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamSpecificationVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamSpecificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionLabel = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BlueprintId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SittingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    MaxScore = table.Column<int>(type: "int", nullable: true),
                    PassingScore = table.Column<int>(type: "int", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RetiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamSpecificationVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamSpecificationVersions_AssessmentBlueprints_BlueprintId",
                        column: x => x.BlueprintId,
                        principalTable: "AssessmentBlueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamSpecificationVersions_ExamSpecifications_ExamSpecificationId",
                        column: x => x.ExamSpecificationId,
                        principalTable: "ExamSpecifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssessmentBlueprintLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AreaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    DifficultyBandLow = table.Column<int>(type: "int", nullable: true),
                    DifficultyBandHigh = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentBlueprintLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentBlueprintLines_AssessmentBlueprintAreas_AreaId",
                        column: x => x.AreaId,
                        principalTable: "AssessmentBlueprintAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentBlueprintLines_LearningTargets_TargetId",
                        column: x => x.TargetId,
                        principalTable: "LearningTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssessmentForms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssessmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormNumber = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    BlueprintId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TimeLimitMs = table.Column<int>(type: "int", nullable: true),
                    SealedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentForms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentForms_AssessmentBlueprints_BlueprintId",
                        column: x => x.BlueprintId,
                        principalTable: "AssessmentBlueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssessmentForms_Assessments_AssessmentId",
                        column: x => x.AssessmentId,
                        principalTable: "Assessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LearnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamSpecificationVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sufficiency = table.Column<int>(type: "int", nullable: false),
                    CoverageRatio = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    WeakestAreaCoverage = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    BasisObservationCount = table.Column<int>(type: "int", nullable: false),
                    ExamLikeObservationCount = table.Column<int>(type: "int", nullable: false),
                    MedianEvidenceAgeDays = table.Column<int>(type: "int", nullable: true),
                    ProficiencyBandLow = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: true),
                    ProficiencyBandHigh = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: true),
                    Confidence = table.Column<int>(type: "int", nullable: true),
                    OutcomeBandLow = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    OutcomeBandHigh = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    CalibrationSampleSize = table.Column<int>(type: "int", nullable: true),
                    MethodKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ComputedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamProjections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamProjections_ExamSpecificationVersions_ExamSpecificationVersionId",
                        column: x => x.ExamSpecificationVersionId,
                        principalTable: "ExamSpecificationVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReportedExamOutcomes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LearnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamSpecificationVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportedScore = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    ReportedGrade = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    MaxScore = table.Column<int>(type: "int", nullable: true),
                    SittingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Verification = table.Column<int>(type: "int", nullable: false),
                    ReportedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConsentedToCalibrationUse = table.Column<bool>(type: "bit", nullable: false),
                    ConsentGrantedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConsentGrantedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConsentWithdrawnAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BlueprintWeightedEstimateAtReport = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: true),
                    CoverageRatioAtReport = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: true),
                    ObservationCountAtReport = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportedExamOutcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportedExamOutcomes_ExamSpecificationVersions_ExamSpecificationVersionId",
                        column: x => x.ExamSpecificationVersionId,
                        principalTable: "ExamSpecificationVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssessmentAdministrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LearnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AdministeredByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeliveryMode = table.Column<int>(type: "int", nullable: false),
                    RetryPermitted = table.Column<bool>(type: "bit", nullable: false),
                    WasAided = table.Column<bool>(type: "bit", nullable: false),
                    TimeLimitMs = table.Column<int>(type: "int", nullable: true),
                    LangId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PointsEarned = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    PointsAvailable = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentAdministrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentAdministrations_AssessmentForms_FormId",
                        column: x => x.FormId,
                        principalTable: "AssessmentForms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssessmentFormItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    ItemVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Points = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: false),
                    BlueprintLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentFormItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentFormItems_AssessmentForms_FormId",
                        column: x => x.FormId,
                        principalTable: "AssessmentForms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentFormItems_ItemVersions_ItemVersionId",
                        column: x => x.ItemVersionId,
                        principalTable: "ItemVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamProjectionGaps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamProjectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AreaKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AreaLabel = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    WeightInExam = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    ObservationCount = table.Column<int>(type: "int", nullable: false),
                    ExamLikeObservationCount = table.Column<int>(type: "int", nullable: false),
                    ObservationsNeeded = table.Column<int>(type: "int", nullable: false),
                    SuggestedNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Rank = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamProjectionGaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamProjectionGaps_ExamProjections_ExamProjectionId",
                        column: x => x.ExamProjectionId,
                        principalTable: "ExamProjections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExamProjectionGaps_LearningTargets_TargetId",
                        column: x => x.TargetId,
                        principalTable: "LearningTargets",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "CompetencyFrameworks",
                columns: new[] { "Id", "AuthorityId", "CreatedAtUtc", "FrameworkKey", "Name", "PublishedAtUtc", "RetiredAtUtc", "VersionLabel" },
                values: new object[] { new Guid("d5b8a3f0-6c42-4e79-9b1a-4f8d2c7e1035"), null, new DateTime(2026, 9, 20, 0, 0, 0, 0, DateTimeKind.Utc), "share7.core", "Share7 authored competencies", new DateTime(2026, 9, 20, 0, 0, 0, 0, DateTimeKind.Utc), null, "v1" });

            migrationBuilder.InsertData(
                table: "CurriculumAuthorities",
                columns: new[] { "Id", "AuthorityKey", "CountryCode", "CreatedAtUtc", "Name", "TrustTier" },
                values: new object[] { new Guid("c4a7f2e9-5b31-4d68-8a09-3e7c1b6d0f24"), "share7", null, new DateTime(2026, 9, 20, 0, 0, 0, 0, DateTimeKind.Utc), "Share7", 0 });

            migrationBuilder.InsertData(
                table: "EvidenceContracts",
                columns: new[] { "Id", "ContractKey", "CreatedAtUtc", "Description", "GameId", "InteractionKind", "ModeId" },
                values: new object[] { new Guid("d3a9e6cb-418a-4f5d-9e57-2c6e0f37b4d9"), "platform.assessment_administration", new DateTime(2026, 9, 20, 0, 0, 0, 0, DateTimeKind.Utc), "An item served inside an AssessmentAdministration, under conditions the server fixed when the sitting was opened and enforced for its duration.", null, "assessment_response", null });

            migrationBuilder.InsertData(
                table: "EvidenceContractVersions",
                columns: new[] { "Id", "AdmittedContexts", "ContractId", "IsIndividuallyAttributable", "Justification", "PublishedAtUtc", "PublishedByUserId", "RequiresFirstEncounter", "RequiresNoRetry", "RequiresUnhinted", "RetiredAtUtc", "StrengthOtherwise", "StrengthWhenControlled", "VersionNumber", "Weight" },
                values: new object[] { new Guid("e4bafd1c-529b-4a6e-8f68-3d7f1a48c5ea"), 31, new Guid("d3a9e6cb-418a-4f5d-9e57-2c6e0f37b4d9"), true, "An AssessmentAdministration fixes its conditions — retries, aiding, delivery mode, time limit — before the first item is served, server-side, and refuses answers after its deadline. The responses it produces are therefore interpretable as an administration under stated conditions rather than as whatever a game happened to allow.", new DateTime(2026, 9, 20, 0, 0, 0, 0, DateTimeKind.Utc), null, true, true, true, null, 2, 3, 1, 1.0m });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentAdministrations_FormId",
                table: "AssessmentAdministrations",
                column: "FormId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentAdministrations_Idempotency",
                table: "AssessmentAdministrations",
                columns: new[] { "LearnerId", "IdempotencyKey" },
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentAdministrations_LearnerId_StartedAtUtc",
                table: "AssessmentAdministrations",
                columns: new[] { "LearnerId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentBlueprintAreas_BlueprintId_AreaKey",
                table: "AssessmentBlueprintAreas",
                columns: new[] { "BlueprintId", "AreaKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentBlueprintLines_AreaId_TargetId",
                table: "AssessmentBlueprintLines",
                columns: new[] { "AreaId", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentBlueprintLines_TargetId",
                table: "AssessmentBlueprintLines",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentBlueprints_BlueprintKey_VersionNumber",
                table: "AssessmentBlueprints",
                columns: new[] { "BlueprintKey", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentBlueprints_FrameworkId",
                table: "AssessmentBlueprints",
                column: "FrameworkId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentFormItems_FormId_Position",
                table: "AssessmentFormItems",
                columns: new[] { "FormId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentFormItems_ItemVersionId",
                table: "AssessmentFormItems",
                column: "ItemVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentForms_AssessmentId_FormNumber",
                table: "AssessmentForms",
                columns: new[] { "AssessmentId", "FormNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentForms_BlueprintId",
                table: "AssessmentForms",
                column: "BlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_Assessments_AssessmentKey",
                table: "Assessments",
                column: "AssessmentKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assessments_BlueprintId",
                table: "Assessments",
                column: "BlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamProjectionGaps_ExamProjectionId_Rank",
                table: "ExamProjectionGaps",
                columns: new[] { "ExamProjectionId", "Rank" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamProjectionGaps_TargetId",
                table: "ExamProjectionGaps",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamProjections_ExamSpecificationVersionId",
                table: "ExamProjections",
                column: "ExamSpecificationVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamProjections_LearnerId_ExamSpecificationVersionId_MethodKey",
                table: "ExamProjections",
                columns: new[] { "LearnerId", "ExamSpecificationVersionId", "MethodKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamSpecifications_AuthorityId",
                table: "ExamSpecifications",
                column: "AuthorityId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamSpecifications_SpecificationKey",
                table: "ExamSpecifications",
                column: "SpecificationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamSpecificationVersions_BlueprintId",
                table: "ExamSpecificationVersions",
                column: "BlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamSpecificationVersions_ExamSpecificationId_VersionLabel",
                table: "ExamSpecificationVersions",
                columns: new[] { "ExamSpecificationId", "VersionLabel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportedExamOutcomes_ExamSpecificationVersionId",
                table: "ReportedExamOutcomes",
                column: "ExamSpecificationVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportedExamOutcomes_LearnerId_ExamSpecificationVersionId",
                table: "ReportedExamOutcomes",
                columns: new[] { "LearnerId", "ExamSpecificationVersionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssessmentAdministrations");

            migrationBuilder.DropTable(
                name: "AssessmentBlueprintLines");

            migrationBuilder.DropTable(
                name: "AssessmentFormItems");

            migrationBuilder.DropTable(
                name: "ExamProjectionGaps");

            migrationBuilder.DropTable(
                name: "ReportedExamOutcomes");

            migrationBuilder.DropTable(
                name: "AssessmentBlueprintAreas");

            migrationBuilder.DropTable(
                name: "AssessmentForms");

            migrationBuilder.DropTable(
                name: "ExamProjections");

            migrationBuilder.DropTable(
                name: "Assessments");

            migrationBuilder.DropTable(
                name: "ExamSpecificationVersions");

            migrationBuilder.DropTable(
                name: "AssessmentBlueprints");

            migrationBuilder.DropTable(
                name: "ExamSpecifications");

            migrationBuilder.DeleteData(
                table: "CompetencyFrameworks",
                keyColumn: "Id",
                keyValue: new Guid("d5b8a3f0-6c42-4e79-9b1a-4f8d2c7e1035"));

            migrationBuilder.DeleteData(
                table: "CurriculumAuthorities",
                keyColumn: "Id",
                keyValue: new Guid("c4a7f2e9-5b31-4d68-8a09-3e7c1b6d0f24"));

            migrationBuilder.DeleteData(
                table: "EvidenceContractVersions",
                keyColumn: "Id",
                keyValue: new Guid("e4bafd1c-529b-4a6e-8f68-3d7f1a48c5ea"));

            migrationBuilder.DeleteData(
                table: "EvidenceContracts",
                keyColumn: "Id",
                keyValue: new Guid("d3a9e6cb-418a-4f5d-9e57-2c6e0f37b4d9"));
        }
    }
}
