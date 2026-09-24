using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EngineAuthoritative : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Terms_GradeId_Order",
                table: "Terms");

            migrationBuilder.DropIndex(
                name: "IX_Subjects_TermId_Order",
                table: "Subjects");

            migrationBuilder.DropIndex(
                name: "IX_Questions_LessonId_Lang_Id_IsActive",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_Lessons_ChapterId_Order",
                table: "Lessons");

            migrationBuilder.DropIndex(
                name: "IX_Chapters_SubjectId_Order",
                table: "Chapters");

            migrationBuilder.AddColumn<DateTime>(
                name: "RetiredAtUtc",
                table: "Terms",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RetiredAtUtc",
                table: "Subjects",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "Questions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "RetiredAtUtc",
                table: "Lessons",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Direction",
                table: "Languages",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "ltr");

            migrationBuilder.AddColumn<bool>(
                name: "IsContentLanguage",
                table: "Languages",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiredToPublish",
                table: "Languages",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Languages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "CurriculumNodes",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "CurriculumNodes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "CurriculumNodes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RetiredAtUtc",
                table: "Chapters",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContentPublications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    LangId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    PublishedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentPublications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentPublications_CurriculumNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "CurriculumNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContentPublications_Languages_LangId",
                        column: x => x.LangId,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CurriculumReadChecks",
                columns: table => new
                {
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    ReadName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Compared = table.Column<long>(type: "bigint", nullable: false),
                    Differed = table.Column<long>(type: "bigint", nullable: false),
                    Failed = table.Column<long>(type: "bigint", nullable: false),
                    LastDifferenceAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastDifferenceSample = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurriculumReadChecks", x => new { x.Day, x.ReadName });
                });

            migrationBuilder.CreateTable(
                name: "PublishedItemSets",
                columns: table => new
                {
                    NodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    LangId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublishedItemSets", x => new { x.NodeId, x.Role, x.LangId });
                    table.ForeignKey(
                        name: "FK_PublishedItemSets_CurriculumNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "CurriculumNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PublishedItemSets_Languages_LangId",
                        column: x => x.LangId,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"),
                columns: new[] { "Direction", "IsContentLanguage", "RequiredToPublish", "SortOrder" },
                values: new object[] { "rtl", true, true, 2 });

            migrationBuilder.UpdateData(
                table: "Languages",
                keyColumn: "Id",
                keyValue: new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"),
                columns: new[] { "Direction", "IsContentLanguage", "RequiredToPublish", "SortOrder" },
                values: new object[] { "ltr", true, true, 1 });

            migrationBuilder.CreateIndex(
                name: "IX_Terms_GradeId_Order",
                table: "Terms",
                columns: new[] { "GradeId", "Order" },
                unique: true,
                filter: "[RetiredAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_TermId_Order",
                table: "Subjects",
                columns: new[] { "TermId", "Order" },
                unique: true,
                filter: "[RetiredAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_LessonId_Role_Lang_Id_IsActive",
                table: "Questions",
                columns: new[] { "LessonId", "Role", "Lang_Id", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_ChapterId_Order",
                table: "Lessons",
                columns: new[] { "ChapterId", "Order" },
                unique: true,
                filter: "[RetiredAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_SubjectId_Order",
                table: "Chapters",
                columns: new[] { "SubjectId", "Order" },
                unique: true,
                filter: "[RetiredAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPublications_LangId",
                table: "ContentPublications",
                column: "LangId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPublications_NodeId_Role_LangId_Version",
                table: "ContentPublications",
                columns: new[] { "NodeId", "Role", "LangId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentPublications_ReleaseId",
                table: "ContentPublications",
                column: "ReleaseId",
                filter: "[ReleaseId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PublishedItemSets_LangId",
                table: "PublishedItemSets",
                column: "LangId");

            // The recovery pool into the item bank (same ids), served versions and publication
            // history into their generic tables, and the node tree marked authoritative. See
            // EngineBackfill for what is derived from what; nothing is guessed.
            migrationBuilder.Sql(EngineBackfill.Sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The recovery pool's rows in Questions are copies — RecoveryQuestions still holds every
            // one of them under the same id — so going back removes the copies and the item identity
            // minted for them, before the column that tells them apart is dropped. Recovery items
            // carry no evidence (nothing answers them through the attempt endpoint).
            // Rows retired since this migration went up keep their order slots; if a live row has
            // since taken one, recreating the unfiltered unique indexes below fails, and that is the
            // right outcome — going back would otherwise reorder the tree without anyone deciding to.
            migrationBuilder.Sql("""
                DELETE c FROM QuestionChoices c JOIN Questions q ON q.Id = c.QuestionId WHERE q.Role <> 0;
                DELETE FROM Questions WHERE Role <> 0;
                DELETE FROM NodeItemMappings WHERE Role = 2;
                DELETE v FROM ItemVersions v JOIN Items i ON i.Id = v.ItemId
                    WHERE i.SourceKey LIKE 'lesson/%/recovery/%'
                      AND NOT EXISTS (SELECT 1 FROM Questions q WHERE q.ItemVersionId = v.Id);
                DELETE i FROM Items i
                    WHERE i.SourceKey LIKE 'lesson/%/recovery/%'
                      AND NOT EXISTS (SELECT 1 FROM ItemVersions v WHERE v.ItemId = i.Id);
                """);

            migrationBuilder.DropTable(
                name: "ContentPublications");

            migrationBuilder.DropTable(
                name: "CurriculumReadChecks");

            migrationBuilder.DropTable(
                name: "PublishedItemSets");

            migrationBuilder.DropIndex(
                name: "IX_Terms_GradeId_Order",
                table: "Terms");

            migrationBuilder.DropIndex(
                name: "IX_Subjects_TermId_Order",
                table: "Subjects");

            migrationBuilder.DropIndex(
                name: "IX_Questions_LessonId_Role_Lang_Id_IsActive",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_Lessons_ChapterId_Order",
                table: "Lessons");

            migrationBuilder.DropIndex(
                name: "IX_Chapters_SubjectId_Order",
                table: "Chapters");

            migrationBuilder.DropColumn(
                name: "RetiredAtUtc",
                table: "Terms");

            migrationBuilder.DropColumn(
                name: "RetiredAtUtc",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "RetiredAtUtc",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "Direction",
                table: "Languages");

            migrationBuilder.DropColumn(
                name: "IsContentLanguage",
                table: "Languages");

            migrationBuilder.DropColumn(
                name: "RequiredToPublish",
                table: "Languages");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Languages");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "CurriculumNodes");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "CurriculumNodes");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "CurriculumNodes");

            migrationBuilder.DropColumn(
                name: "RetiredAtUtc",
                table: "Chapters");

            migrationBuilder.CreateIndex(
                name: "IX_Terms_GradeId_Order",
                table: "Terms",
                columns: new[] { "GradeId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_TermId_Order",
                table: "Subjects",
                columns: new[] { "TermId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Questions_LessonId_Lang_Id_IsActive",
                table: "Questions",
                columns: new[] { "LessonId", "Lang_Id", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_ChapterId_Order",
                table: "Lessons",
                columns: new[] { "ChapterId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_SubjectId_Order",
                table: "Chapters",
                columns: new[] { "SubjectId", "Order" },
                unique: true);
        }
    }
}
