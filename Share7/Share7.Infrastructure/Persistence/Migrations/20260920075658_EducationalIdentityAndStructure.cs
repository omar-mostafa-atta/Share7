using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EducationalIdentityAndStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LearnerResponses_Questions_ItemVersionId",
                table: "LearnerResponses");

            migrationBuilder.DropIndex(
                name: "IX_LearnerResponses_LearnerId_ItemVersionId_OccurredAtUtc",
                table: "LearnerResponses");

            migrationBuilder.AddColumn<Guid>(
                name: "ItemVersionId",
                table: "Questions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ItemId",
                table: "LearnerResponses",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ItemLocalizationId",
                table: "LearnerResponses",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CompetencyFrameworks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FrameworkKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AuthorityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VersionLabel = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RetiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetencyFrameworks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CurriculumAuthorities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorityKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CountryCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    TrustTier = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurriculumAuthorities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItemBanks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OwnerScope = table.Column<int>(type: "int", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewPolicy = table.Column<int>(type: "int", nullable: false),
                    MaxEvidenceStrength = table.Column<int>(type: "int", nullable: false),
                    PoolsStatisticsGlobally = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemBanks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearningTargets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FrameworkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TargetKindKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsPlaceholder = table.Column<bool>(type: "bit", nullable: false),
                    ReviewState = table.Column<int>(type: "int", nullable: false),
                    DifficultyBand = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RetiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningTargets_CompetencyFrameworks_FrameworkId",
                        column: x => x.FrameworkId,
                        principalTable: "CompetencyFrameworks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Curricula",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurriculumKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AuthorityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Curricula", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Curricula_CurriculumAuthorities_AuthorityId",
                        column: x => x.AuthorityId,
                        principalTable: "CurriculumAuthorities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemBankId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsAnchor = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RetiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Items_ItemBanks_ItemBankId",
                        column: x => x.ItemBankId,
                        principalTable: "ItemBanks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LearningTargetAlignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceTargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AlignedTargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Strength = table.Column<int>(type: "int", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningTargetAlignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningTargetAlignments_LearningTargets_AlignedTargetId",
                        column: x => x.AlignedTargetId,
                        principalTable: "LearningTargets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LearningTargetAlignments_LearningTargets_SourceTargetId",
                        column: x => x.SourceTargetId,
                        principalTable: "LearningTargets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LearningTargetEdges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromTargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToTargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EdgeKind = table.Column<int>(type: "int", nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningTargetEdges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningTargetEdges_LearningTargets_FromTargetId",
                        column: x => x.FromTargetId,
                        principalTable: "LearningTargets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LearningTargetEdges_LearningTargets_ToTargetId",
                        column: x => x.ToTargetId,
                        principalTable: "LearningTargets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LearningTargetTranslations",
                columns: table => new
                {
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LangId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Statement = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningTargetTranslations", x => new { x.TargetId, x.LangId });
                    table.ForeignKey(
                        name: "FK_LearningTargetTranslations_Languages_LangId",
                        column: x => x.LangId,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LearningTargetTranslations_LearningTargets_TargetId",
                        column: x => x.TargetId,
                        principalTable: "LearningTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NodeTargetMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurriculumVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Emphasis = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeTargetMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NodeTargetMappings_LearningTargets_TargetId",
                        column: x => x.TargetId,
                        principalTable: "LearningTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CurriculumVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurriculumId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionLabel = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsAuthoritative = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurriculumVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CurriculumVersions_Curricula_CurriculumId",
                        column: x => x.CurriculumId,
                        principalTable: "Curricula",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemTargetMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Emphasis = table.Column<decimal>(type: "decimal(6,4)", precision: 6, scale: 4, nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemTargetMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemTargetMappings_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemTargetMappings_LearningTargets_TargetId",
                        column: x => x.TargetId,
                        principalTable: "LearningTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    ItemKindKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ResponseSpec = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ScoringSpec = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PsychometricContinuity = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RetiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemVersions_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NodeItemMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurriculumVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RemovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeItemMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NodeItemMappings_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CurriculumNodeKinds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurriculumVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KindKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Depth = table.Column<int>(type: "int", nullable: false),
                    ParentKindKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsPlayable = table.Column<bool>(type: "bit", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurriculumNodeKinds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CurriculumNodeKinds_CurriculumVersions_CurriculumVersionId",
                        column: x => x.CurriculumVersionId,
                        principalTable: "CurriculumVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Enrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LearnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurriculumVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlacementNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Source = table.Column<int>(type: "int", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Enrollments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Enrollments_CurriculumVersions_CurriculumVersionId",
                        column: x => x.CurriculumVersionId,
                        principalTable: "CurriculumVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CurriculumNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurriculumVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NodeKindId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KindKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Depth = table.Column<int>(type: "int", nullable: false),
                    Path = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    IsPlayable = table.Column<bool>(type: "bit", nullable: false),
                    LegacySource = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RetiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurriculumNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CurriculumNodes_CurriculumNodeKinds_NodeKindId",
                        column: x => x.NodeKindId,
                        principalTable: "CurriculumNodeKinds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CurriculumNodes_CurriculumNodes_ParentNodeId",
                        column: x => x.ParentNodeId,
                        principalTable: "CurriculumNodes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CurriculumNodes_CurriculumVersions_CurriculumVersionId",
                        column: x => x.CurriculumVersionId,
                        principalTable: "CurriculumVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CurriculumNodeTranslations",
                columns: table => new
                {
                    NodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LangId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurriculumNodeTranslations", x => new { x.NodeId, x.LangId });
                    table.ForeignKey(
                        name: "FK_CurriculumNodeTranslations_CurriculumNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "CurriculumNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CurriculumNodeTranslations_Languages_LangId",
                        column: x => x.LangId,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "CompetencyFrameworks",
                columns: new[] { "Id", "AuthorityId", "CreatedAtUtc", "FrameworkKey", "Name", "PublishedAtUtc", "RetiredAtUtc", "VersionLabel" },
                values: new object[] { new Guid("b8e4f6a0-9d71-4c25-83b1-0f5d2c419688"), null, new DateTime(2026, 9, 20, 0, 0, 0, 0, DateTimeKind.Utc), "share7.lesson_placeholder", "Lesson placeholders (pre-framework)", new DateTime(2026, 9, 20, 0, 0, 0, 0, DateTimeKind.Utc), null, "bootstrap" });

            migrationBuilder.InsertData(
                table: "CurriculumAuthorities",
                columns: new[] { "Id", "AuthorityKey", "CountryCode", "CreatedAtUtc", "Name", "TrustTier" },
                values: new object[] { new Guid("e5b1c3d7-6a48-4f92-b0de-7c2a9f1863b5"), "eg.moe", "EG", new DateTime(2026, 9, 20, 0, 0, 0, 0, DateTimeKind.Utc), "Egyptian Ministry of Education", 2 });

            migrationBuilder.InsertData(
                table: "ItemBanks",
                columns: new[] { "Id", "BankKey", "CreatedAtUtc", "MaxEvidenceStrength", "Name", "OwnerId", "OwnerScope", "PoolsStatisticsGlobally", "ReviewPolicy" },
                values: new object[] { new Guid("d3a9e6cb-418a-4f5d-b079-2c6e0f37b4d8"), "platform.curriculum", new DateTime(2026, 9, 20, 0, 0, 0, 0, DateTimeKind.Utc), 3, "Share7 curriculum content", null, 0, true, 1 });

            migrationBuilder.InsertData(
                table: "Curricula",
                columns: new[] { "Id", "AuthorityId", "CreatedAtUtc", "CurriculumKey", "Name" },
                values: new object[] { new Guid("f6c2d4e8-7b59-4a03-a1ef-8d3b0a2974c6"), new Guid("e5b1c3d7-6a48-4f92-b0de-7c2a9f1863b5"), new DateTime(2026, 9, 20, 0, 0, 0, 0, DateTimeKind.Utc), "eg.national", "Egyptian National Curriculum" });

            migrationBuilder.InsertData(
                table: "CurriculumVersions",
                columns: new[] { "Id", "CreatedAtUtc", "CurriculumId", "EffectiveFrom", "EffectiveTo", "IsAuthoritative", "PublishedAtUtc", "VersionLabel" },
                values: new object[] { new Guid("a7d3e5f9-8c60-4b14-92a0-9e4c1b308577"), new DateTime(2026, 9, 20, 0, 0, 0, 0, DateTimeKind.Utc), new Guid("f6c2d4e8-7b59-4a03-a1ef-8d3b0a2974c6"), null, null, false, new DateTime(2026, 9, 20, 0, 0, 0, 0, DateTimeKind.Utc), "as-migrated" });

            migrationBuilder.InsertData(
                table: "CurriculumNodeKinds",
                columns: new[] { "Id", "CurriculumVersionId", "Depth", "DisplayName", "IsPlayable", "KindKey", "Order", "ParentKindKey" },
                values: new object[,]
                {
                    { new Guid("13d0c0b1-0000-4000-8000-000000000001"), new Guid("a7d3e5f9-8c60-4b14-92a0-9e4c1b308577"), 0, "Grade", false, "grade", 1, null },
                    { new Guid("13d0c0b1-0000-4000-8000-000000000002"), new Guid("a7d3e5f9-8c60-4b14-92a0-9e4c1b308577"), 1, "Term", false, "term", 2, "grade" },
                    { new Guid("13d0c0b1-0000-4000-8000-000000000003"), new Guid("a7d3e5f9-8c60-4b14-92a0-9e4c1b308577"), 2, "Subject", false, "subject", 3, "term" },
                    { new Guid("13d0c0b1-0000-4000-8000-000000000004"), new Guid("a7d3e5f9-8c60-4b14-92a0-9e4c1b308577"), 3, "Chapter", false, "chapter", 4, "subject" },
                    { new Guid("13d0c0b1-0000-4000-8000-000000000005"), new Guid("a7d3e5f9-8c60-4b14-92a0-9e4c1b308577"), 4, "Lesson", true, "lesson", 5, "chapter" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Questions_ItemVersionId_Lang_Id",
                table: "Questions",
                columns: new[] { "ItemVersionId", "Lang_Id" });

            migrationBuilder.CreateIndex(
                name: "IX_LearnerResponses_ItemId",
                table: "LearnerResponses",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerResponses_ItemLocalizationId",
                table: "LearnerResponses",
                column: "ItemLocalizationId");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerResponses_LearnerId_ItemId_OccurredAtUtc",
                table: "LearnerResponses",
                columns: new[] { "LearnerId", "ItemId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CompetencyFrameworks_FrameworkKey",
                table: "CompetencyFrameworks",
                column: "FrameworkKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Curricula_AuthorityId",
                table: "Curricula",
                column: "AuthorityId");

            migrationBuilder.CreateIndex(
                name: "IX_Curricula_CurriculumKey",
                table: "Curricula",
                column: "CurriculumKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumAuthorities_AuthorityKey",
                table: "CurriculumAuthorities",
                column: "AuthorityKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumNodeKinds_CurriculumVersionId_KindKey",
                table: "CurriculumNodeKinds",
                columns: new[] { "CurriculumVersionId", "KindKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumNodes_CurriculumVersionId_KindKey",
                table: "CurriculumNodes",
                columns: new[] { "CurriculumVersionId", "KindKey" });

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumNodes_CurriculumVersionId_ParentNodeId_Order",
                table: "CurriculumNodes",
                columns: new[] { "CurriculumVersionId", "ParentNodeId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumNodes_NodeKindId",
                table: "CurriculumNodes",
                column: "NodeKindId");

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumNodes_ParentNodeId",
                table: "CurriculumNodes",
                column: "ParentNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumNodes_Path",
                table: "CurriculumNodes",
                column: "Path");

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumNodeTranslations_LangId",
                table: "CurriculumNodeTranslations",
                column: "LangId");

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumVersions_CurriculumId_VersionLabel",
                table: "CurriculumVersions",
                columns: new[] { "CurriculumId", "VersionLabel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_CurriculumVersionId",
                table: "Enrollments",
                column: "CurriculumVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_LearnerId_ActivePrimary",
                table: "Enrollments",
                column: "LearnerId",
                unique: true,
                filter: "[IsPrimary] = 1 AND [EndedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_LearnerId_CurriculumVersionId_StartedAtUtc",
                table: "Enrollments",
                columns: new[] { "LearnerId", "CurriculumVersionId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemBanks_BankKey",
                table: "ItemBanks",
                column: "BankKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Items_IsAnchor",
                table: "Items",
                column: "IsAnchor",
                filter: "[IsAnchor] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Items_ItemBankId",
                table: "Items",
                column: "ItemBankId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_SourceKey",
                table: "Items",
                column: "SourceKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemTargetMappings_ItemId_Primary",
                table: "ItemTargetMappings",
                column: "ItemId",
                unique: true,
                filter: "[IsPrimary] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ItemTargetMappings_ItemId_TargetId",
                table: "ItemTargetMappings",
                columns: new[] { "ItemId", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemTargetMappings_TargetId",
                table: "ItemTargetMappings",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemVersions_ItemId_VersionNumber",
                table: "ItemVersions",
                columns: new[] { "ItemId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearningTargetAlignments_AlignedTargetId",
                table: "LearningTargetAlignments",
                column: "AlignedTargetId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningTargetAlignments_SourceTargetId_AlignedTargetId",
                table: "LearningTargetAlignments",
                columns: new[] { "SourceTargetId", "AlignedTargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearningTargetEdges_FromTargetId_ToTargetId_EdgeKind",
                table: "LearningTargetEdges",
                columns: new[] { "FromTargetId", "ToTargetId", "EdgeKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearningTargetEdges_ToTargetId",
                table: "LearningTargetEdges",
                column: "ToTargetId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningTargets_FrameworkId_IsPlaceholder",
                table: "LearningTargets",
                columns: new[] { "FrameworkId", "IsPlaceholder" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningTargets_FrameworkId_TargetKey",
                table: "LearningTargets",
                columns: new[] { "FrameworkId", "TargetKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearningTargetTranslations_LangId",
                table: "LearningTargetTranslations",
                column: "LangId");

            migrationBuilder.CreateIndex(
                name: "IX_NodeItemMappings_CurriculumVersionId_NodeId_ItemId_Role",
                table: "NodeItemMappings",
                columns: new[] { "CurriculumVersionId", "NodeId", "ItemId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NodeItemMappings_ItemId",
                table: "NodeItemMappings",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_NodeTargetMappings_CurriculumVersionId_NodeId_TargetId",
                table: "NodeTargetMappings",
                columns: new[] { "CurriculumVersionId", "NodeId", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NodeTargetMappings_TargetId",
                table: "NodeTargetMappings",
                column: "TargetId");

            // ---- data ----------------------------------------------------------------
            // Runs before the foreign keys, because the columns above were added NOT NULL with a
            // zero default and every existing row has to be given its real identity first. See
            // EducationalBackfill for what is derived and why none of it is guessed.
            migrationBuilder.Sql(EducationalBackfill.Sql);

            migrationBuilder.AddForeignKey(
                name: "FK_LearnerResponses_ItemVersions_ItemVersionId",
                table: "LearnerResponses",
                column: "ItemVersionId",
                principalTable: "ItemVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LearnerResponses_Items_ItemId",
                table: "LearnerResponses",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LearnerResponses_Questions_ItemLocalizationId",
                table: "LearnerResponses",
                column: "ItemLocalizationId",
                principalTable: "Questions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Questions_ItemVersions_ItemVersionId",
                table: "Questions",
                column: "ItemVersionId",
                principalTable: "ItemVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LearnerResponses_ItemVersions_ItemVersionId",
                table: "LearnerResponses");

            migrationBuilder.DropForeignKey(
                name: "FK_LearnerResponses_Items_ItemId",
                table: "LearnerResponses");

            migrationBuilder.DropForeignKey(
                name: "FK_LearnerResponses_Questions_ItemLocalizationId",
                table: "LearnerResponses");

            migrationBuilder.DropForeignKey(
                name: "FK_Questions_ItemVersions_ItemVersionId",
                table: "Questions");

            migrationBuilder.DropTable(
                name: "CurriculumNodeTranslations");

            migrationBuilder.DropTable(
                name: "Enrollments");

            migrationBuilder.DropTable(
                name: "ItemTargetMappings");

            migrationBuilder.DropTable(
                name: "ItemVersions");

            migrationBuilder.DropTable(
                name: "LearningTargetAlignments");

            migrationBuilder.DropTable(
                name: "LearningTargetEdges");

            migrationBuilder.DropTable(
                name: "LearningTargetTranslations");

            migrationBuilder.DropTable(
                name: "NodeItemMappings");

            migrationBuilder.DropTable(
                name: "NodeTargetMappings");

            migrationBuilder.DropTable(
                name: "CurriculumNodes");

            migrationBuilder.DropTable(
                name: "Items");

            migrationBuilder.DropTable(
                name: "LearningTargets");

            migrationBuilder.DropTable(
                name: "CurriculumNodeKinds");

            migrationBuilder.DropTable(
                name: "ItemBanks");

            migrationBuilder.DropTable(
                name: "CompetencyFrameworks");

            migrationBuilder.DropTable(
                name: "CurriculumVersions");

            migrationBuilder.DropTable(
                name: "Curricula");

            migrationBuilder.DropTable(
                name: "CurriculumAuthorities");

            migrationBuilder.DropIndex(
                name: "IX_Questions_ItemVersionId_Lang_Id",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_LearnerResponses_ItemId",
                table: "LearnerResponses");

            migrationBuilder.DropIndex(
                name: "IX_LearnerResponses_ItemLocalizationId",
                table: "LearnerResponses");

            migrationBuilder.DropIndex(
                name: "IX_LearnerResponses_LearnerId_ItemId_OccurredAtUtc",
                table: "LearnerResponses");

            migrationBuilder.DropColumn(
                name: "ItemVersionId",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "ItemId",
                table: "LearnerResponses");

            migrationBuilder.DropColumn(
                name: "ItemLocalizationId",
                table: "LearnerResponses");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerResponses_LearnerId_ItemVersionId_OccurredAtUtc",
                table: "LearnerResponses",
                columns: new[] { "LearnerId", "ItemVersionId", "OccurredAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_LearnerResponses_Questions_ItemVersionId",
                table: "LearnerResponses",
                column: "ItemVersionId",
                principalTable: "Questions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
