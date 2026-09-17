using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CurriculumSharedTreeAndTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------------------------------------------------------------------------
            // DESTRUCTIVE, AND IT RUNS UNATTENDED.
            //
            // Program.cs calls MigrateAsync() on every startup, so this executes against the
            // hosted database the moment a new build boots there — there is no manual
            // confirmation step in front of it.
            //
            // The old tree stored one row per node *per language* with no link between the
            // English and Arabic sides. Nothing can pair them automatically, so the tree
            // cannot be converted in place; it is dropped and rebuilt through the admin API.
            // The content it removes was sample data, not real curriculum.
            //
            // Three separate reasons the schema change cannot proceed over existing rows:
            //   1. Every grade row is replaced (new ids, Egyptian ladder), and
            //      StudentProfiles.GradeId is a Restrict FK — the DeleteData calls below
            //      would fail with an FK violation while any profile still points at one.
            //   2. Order arrives defaulted to 0 on every existing row, which immediately
            //      violates the new unique (ParentId, Order) indexes.
            //   3. Names move into the translation tables, so surviving nodes would be left
            //      nameless with no way to recover the text that was dropped with the column.
            //
            // Deleted parent-first rather than relying on the cascades, so this still holds
            // if a delete rule is ever loosened. Accounts, credentials and roles are NOT
            // touched — only the profile rows that carry a grade. Affected students go back
            // through complete-profile once, which the client already handles via
            // isProfileComplete.
            // ---------------------------------------------------------------------------
            migrationBuilder.Sql("DELETE FROM [StudentProfiles];");
            migrationBuilder.Sql("DELETE FROM [LessonQuestionUploads];");
            migrationBuilder.Sql("DELETE FROM [QuestionChoices];");
            migrationBuilder.Sql("DELETE FROM [Questions];");
            migrationBuilder.Sql("DELETE FROM [Lessons];");
            migrationBuilder.Sql("DELETE FROM [Chapters];");
            migrationBuilder.Sql("DELETE FROM [Subjects];");
            migrationBuilder.Sql("DELETE FROM [Terms];");
            migrationBuilder.Sql("DELETE FROM [Grades];");

            migrationBuilder.DropForeignKey(
                name: "FK_Chapters_Languages_Lang_Id",
                table: "Chapters");

            migrationBuilder.DropForeignKey(
                name: "FK_Grades_Languages_Lang_Id",
                table: "Grades");

            migrationBuilder.DropForeignKey(
                name: "FK_Lessons_Languages_Lang_Id",
                table: "Lessons");

            migrationBuilder.DropForeignKey(
                name: "FK_Subjects_Languages_Lang_Id",
                table: "Subjects");

            migrationBuilder.DropForeignKey(
                name: "FK_Terms_Languages_Lang_Id",
                table: "Terms");

            migrationBuilder.DropIndex(
                name: "IX_Terms_GradeId",
                table: "Terms");

            migrationBuilder.DropIndex(
                name: "IX_Terms_Lang_Id",
                table: "Terms");

            migrationBuilder.DropIndex(
                name: "IX_Subjects_Lang_Id",
                table: "Subjects");

            migrationBuilder.DropIndex(
                name: "IX_Subjects_TermId",
                table: "Subjects");

            migrationBuilder.DropIndex(
                name: "IX_Questions_LessonId_IsActive",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_Lessons_ChapterId",
                table: "Lessons");

            migrationBuilder.DropIndex(
                name: "IX_Lessons_Lang_Id",
                table: "Lessons");

            migrationBuilder.DropIndex(
                name: "IX_LessonQuestionUploads_LessonId_Version",
                table: "LessonQuestionUploads");

            migrationBuilder.DropIndex(
                name: "IX_Grades_Lang_Id",
                table: "Grades");

            migrationBuilder.DropIndex(
                name: "IX_Chapters_Lang_Id",
                table: "Chapters");

            migrationBuilder.DropIndex(
                name: "IX_Chapters_SubjectId",
                table: "Chapters");

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("07e2c9f6-d38a-45e7-f16b-923e80230f7d"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("18f3d007-e49b-46f8-021c-a34f9134108e"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("2904e118-f5ac-4709-132d-b450a245219f"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("2b004d42-ca9c-4823-ac5b-1cb59cba0467"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("38111b66-de42-4adc-b20c-464b1dd2a9d1"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("3a15f229-06bd-481a-243e-c561b35632a0"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("4b260330-17ce-492b-354f-d672c46743b1"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("52c97299-9356-46d5-87e9-fc1ac17d5c14"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("5c371441-28df-4a3c-4650-e783d57854c2"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("76e10c16-da74-4731-8818-448262946b70"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("895f300d-21b4-4167-afc7-6c06677abf0e"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("a1e6c390-7d24-4f81-9b05-3c8e2a6f4d17"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("b14d62e5-56ee-42a4-916a-5554d811f72f"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("b2f7d4a1-8e35-4092-ac16-4d9f3b7e5a28"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("c2287d7b-e111-45ef-ac10-a3669c8a5eeb"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("c3a8e5b2-9f46-41a3-bd27-5e0a4c8f6b39"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("c3f4e414-cde4-4929-a235-d4f09b5f748d"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("cc203a51-cc8c-4814-ae5e-587c9903e17c"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("d4b9f6c3-a057-42b4-ce38-6f1b5d907c4a"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("de6e690a-9bbf-4e32-afea-7e36942f7957"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("e5c0a7d4-b168-43c5-df49-701c6e018d5b"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("ee0bc8b5-de4a-4a25-afaa-cfdec9e9e27a"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("f4313c32-260c-4e55-8877-c68b9d5ae33d"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("f6d1b8e5-c279-44d6-e05a-812d7f129e6c"));

            migrationBuilder.DropColumn(
                name: "Lang_Id",
                table: "Terms");

            migrationBuilder.DropColumn(
                name: "Term",
                table: "Terms");

            migrationBuilder.DropColumn(
                name: "Lang_Id",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "Subject",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "Lang_Id",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "Lesson",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "QuestionsVersion",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "Grade",
                table: "Grades");

            migrationBuilder.DropColumn(
                name: "Lang_Id",
                table: "Grades");

            migrationBuilder.DropColumn(
                name: "Chapter",
                table: "Chapters");

            migrationBuilder.DropColumn(
                name: "Lang_Id",
                table: "Chapters");

            migrationBuilder.AddColumn<int>(
                name: "Order",
                table: "Terms",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Order",
                table: "Subjects",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Order",
                table: "Lessons",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "Lang_Id",
                table: "LessonQuestionUploads",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "Order",
                table: "Grades",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Order",
                table: "Chapters",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ChapterTranslations",
                columns: table => new
                {
                    ChapterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Lang_Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChapterTranslations", x => new { x.ChapterId, x.Lang_Id });
                    table.ForeignKey(
                        name: "FK_ChapterTranslations_Chapters_ChapterId",
                        column: x => x.ChapterId,
                        principalTable: "Chapters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChapterTranslations_Languages_Lang_Id",
                        column: x => x.Lang_Id,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GradeTranslations",
                columns: table => new
                {
                    GradeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Lang_Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeTranslations", x => new { x.GradeId, x.Lang_Id });
                    table.ForeignKey(
                        name: "FK_GradeTranslations_Grades_GradeId",
                        column: x => x.GradeId,
                        principalTable: "Grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GradeTranslations_Languages_Lang_Id",
                        column: x => x.Lang_Id,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LessonQuestionSets",
                columns: table => new
                {
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Lang_Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonQuestionSets", x => new { x.LessonId, x.Lang_Id });
                    table.ForeignKey(
                        name: "FK_LessonQuestionSets_Languages_Lang_Id",
                        column: x => x.Lang_Id,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LessonQuestionSets_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LessonTranslations",
                columns: table => new
                {
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Lang_Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonTranslations", x => new { x.LessonId, x.Lang_Id });
                    table.ForeignKey(
                        name: "FK_LessonTranslations_Languages_Lang_Id",
                        column: x => x.Lang_Id,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LessonTranslations_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubjectTranslations",
                columns: table => new
                {
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Lang_Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubjectTranslations", x => new { x.SubjectId, x.Lang_Id });
                    table.ForeignKey(
                        name: "FK_SubjectTranslations_Languages_Lang_Id",
                        column: x => x.Lang_Id,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubjectTranslations_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TermTranslations",
                columns: table => new
                {
                    TermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Lang_Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TermTranslations", x => new { x.TermId, x.Lang_Id });
                    table.ForeignKey(
                        name: "FK_TermTranslations_Languages_Lang_Id",
                        column: x => x.Lang_Id,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TermTranslations_Terms_TermId",
                        column: x => x.TermId,
                        principalTable: "Terms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Grades",
                columns: new[] { "Id", "Order" },
                values: new object[,]
                {
                    { new Guid("a0000000-0000-4000-8000-000000000001"), 1 },
                    { new Guid("a0000000-0000-4000-8000-000000000002"), 2 },
                    { new Guid("a0000000-0000-4000-8000-000000000003"), 3 },
                    { new Guid("a0000000-0000-4000-8000-000000000004"), 4 },
                    { new Guid("a0000000-0000-4000-8000-000000000005"), 5 },
                    { new Guid("a0000000-0000-4000-8000-000000000006"), 6 },
                    { new Guid("a0000000-0000-4000-8000-000000000007"), 7 },
                    { new Guid("a0000000-0000-4000-8000-000000000008"), 8 },
                    { new Guid("a0000000-0000-4000-8000-000000000009"), 9 },
                    { new Guid("a0000000-0000-4000-8000-00000000000a"), 10 },
                    { new Guid("a0000000-0000-4000-8000-00000000000b"), 11 },
                    { new Guid("a0000000-0000-4000-8000-00000000000c"), 12 },
                    { new Guid("a0000000-0000-4000-8000-00000000000d"), 13 },
                    { new Guid("a0000000-0000-4000-8000-00000000000e"), 14 }
                });

            migrationBuilder.InsertData(
                table: "GradeTranslations",
                columns: new[] { "GradeId", "Lang_Id", "Name" },
                values: new object[,]
                {
                    { new Guid("a0000000-0000-4000-8000-000000000001"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الروضة الأولى" },
                    { new Guid("a0000000-0000-4000-8000-000000000001"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "KG1" },
                    { new Guid("a0000000-0000-4000-8000-000000000002"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الروضة الثانية" },
                    { new Guid("a0000000-0000-4000-8000-000000000002"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "KG2" },
                    { new Guid("a0000000-0000-4000-8000-000000000003"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الأول الابتدائي" },
                    { new Guid("a0000000-0000-4000-8000-000000000003"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Primary One" },
                    { new Guid("a0000000-0000-4000-8000-000000000004"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الثاني الابتدائي" },
                    { new Guid("a0000000-0000-4000-8000-000000000004"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Primary Two" },
                    { new Guid("a0000000-0000-4000-8000-000000000005"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الثالث الابتدائي" },
                    { new Guid("a0000000-0000-4000-8000-000000000005"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Primary Three" },
                    { new Guid("a0000000-0000-4000-8000-000000000006"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الرابع الابتدائي" },
                    { new Guid("a0000000-0000-4000-8000-000000000006"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Primary Four" },
                    { new Guid("a0000000-0000-4000-8000-000000000007"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الخامس الابتدائي" },
                    { new Guid("a0000000-0000-4000-8000-000000000007"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Primary Five" },
                    { new Guid("a0000000-0000-4000-8000-000000000008"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف السادس الابتدائي" },
                    { new Guid("a0000000-0000-4000-8000-000000000008"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Primary Six" },
                    { new Guid("a0000000-0000-4000-8000-000000000009"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الأول الإعدادي" },
                    { new Guid("a0000000-0000-4000-8000-000000000009"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Preparatory One" },
                    { new Guid("a0000000-0000-4000-8000-00000000000a"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الثاني الإعدادي" },
                    { new Guid("a0000000-0000-4000-8000-00000000000a"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Preparatory Two" },
                    { new Guid("a0000000-0000-4000-8000-00000000000b"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الثالث الإعدادي" },
                    { new Guid("a0000000-0000-4000-8000-00000000000b"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Preparatory Three" },
                    { new Guid("a0000000-0000-4000-8000-00000000000c"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الأول الثانوي" },
                    { new Guid("a0000000-0000-4000-8000-00000000000c"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Secondary One" },
                    { new Guid("a0000000-0000-4000-8000-00000000000d"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الثاني الثانوي" },
                    { new Guid("a0000000-0000-4000-8000-00000000000d"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Secondary Two" },
                    { new Guid("a0000000-0000-4000-8000-00000000000e"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الثالث الثانوي" },
                    { new Guid("a0000000-0000-4000-8000-00000000000e"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Secondary Three" }
                });

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
                name: "IX_LessonQuestionUploads_Lang_Id",
                table: "LessonQuestionUploads",
                column: "Lang_Id");

            migrationBuilder.CreateIndex(
                name: "IX_LessonQuestionUploads_LessonId_Lang_Id_Version",
                table: "LessonQuestionUploads",
                columns: new[] { "LessonId", "Lang_Id", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Grades_Order",
                table: "Grades",
                column: "Order",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_SubjectId_Order",
                table: "Chapters",
                columns: new[] { "SubjectId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChapterTranslations_Lang_Id_Name",
                table: "ChapterTranslations",
                columns: new[] { "Lang_Id", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_GradeTranslations_Lang_Id_Name",
                table: "GradeTranslations",
                columns: new[] { "Lang_Id", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonQuestionSets_Lang_Id",
                table: "LessonQuestionSets",
                column: "Lang_Id");

            migrationBuilder.CreateIndex(
                name: "IX_LessonTranslations_Lang_Id_Name",
                table: "LessonTranslations",
                columns: new[] { "Lang_Id", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_SubjectTranslations_Lang_Id_Name",
                table: "SubjectTranslations",
                columns: new[] { "Lang_Id", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_TermTranslations_Lang_Id_Name",
                table: "TermTranslations",
                columns: new[] { "Lang_Id", "Name" });

            migrationBuilder.AddForeignKey(
                name: "FK_LessonQuestionUploads_Languages_Lang_Id",
                table: "LessonQuestionUploads",
                column: "Lang_Id",
                principalTable: "Languages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restores the schema, not the data. The curriculum rows and student profiles
            // deleted by Up() are gone, and the language-partitioned tree they belonged to
            // cannot be reconstructed from the shared one. Reverting leaves empty tables plus
            // the 24 originally seeded grade rows.
            migrationBuilder.DropForeignKey(
                name: "FK_LessonQuestionUploads_Languages_Lang_Id",
                table: "LessonQuestionUploads");

            migrationBuilder.DropTable(
                name: "ChapterTranslations");

            migrationBuilder.DropTable(
                name: "GradeTranslations");

            migrationBuilder.DropTable(
                name: "LessonQuestionSets");

            migrationBuilder.DropTable(
                name: "LessonTranslations");

            migrationBuilder.DropTable(
                name: "SubjectTranslations");

            migrationBuilder.DropTable(
                name: "TermTranslations");

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
                name: "IX_LessonQuestionUploads_Lang_Id",
                table: "LessonQuestionUploads");

            migrationBuilder.DropIndex(
                name: "IX_LessonQuestionUploads_LessonId_Lang_Id_Version",
                table: "LessonQuestionUploads");

            migrationBuilder.DropIndex(
                name: "IX_Grades_Order",
                table: "Grades");

            migrationBuilder.DropIndex(
                name: "IX_Chapters_SubjectId_Order",
                table: "Chapters");

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000001"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000002"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000003"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000004"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000005"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000006"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000007"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000008"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-4000-8000-000000000009"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-4000-8000-00000000000a"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-4000-8000-00000000000b"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-4000-8000-00000000000c"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-4000-8000-00000000000d"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("a0000000-0000-4000-8000-00000000000e"));

            migrationBuilder.DropColumn(
                name: "Order",
                table: "Terms");

            migrationBuilder.DropColumn(
                name: "Order",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "Order",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "Lang_Id",
                table: "LessonQuestionUploads");

            migrationBuilder.DropColumn(
                name: "Order",
                table: "Grades");

            migrationBuilder.DropColumn(
                name: "Order",
                table: "Chapters");

            migrationBuilder.AddColumn<Guid>(
                name: "Lang_Id",
                table: "Terms",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Term",
                table: "Terms",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "Lang_Id",
                table: "Subjects",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Subject",
                table: "Subjects",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "Lang_Id",
                table: "Lessons",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Lesson",
                table: "Lessons",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "QuestionsVersion",
                table: "Lessons",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Grade",
                table: "Grades",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "Lang_Id",
                table: "Grades",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Chapter",
                table: "Chapters",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "Lang_Id",
                table: "Chapters",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.InsertData(
                table: "Grades",
                columns: new[] { "Id", "Lang_Id", "Grade" },
                values: new object[,]
                {
                    { new Guid("07e2c9f6-d38a-45e7-f16b-923e80230f7d"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف السابع" },
                    { new Guid("18f3d007-e49b-46f8-021c-a34f9134108e"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الثامن" },
                    { new Guid("2904e118-f5ac-4709-132d-b450a245219f"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف التاسع" },
                    { new Guid("2b004d42-ca9c-4823-ac5b-1cb59cba0467"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Grade 4" },
                    { new Guid("38111b66-de42-4adc-b20c-464b1dd2a9d1"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Grade 12" },
                    { new Guid("3a15f229-06bd-481a-243e-c561b35632a0"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف العاشر" },
                    { new Guid("4b260330-17ce-492b-354f-d672c46743b1"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الحادي عشر" },
                    { new Guid("52c97299-9356-46d5-87e9-fc1ac17d5c14"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Grade 3" },
                    { new Guid("5c371441-28df-4a3c-4650-e783d57854c2"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الثاني عشر" },
                    { new Guid("76e10c16-da74-4731-8818-448262946b70"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Grade 6" },
                    { new Guid("895f300d-21b4-4167-afc7-6c06677abf0e"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Grade 9" },
                    { new Guid("a1e6c390-7d24-4f81-9b05-3c8e2a6f4d17"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الأول" },
                    { new Guid("b14d62e5-56ee-42a4-916a-5554d811f72f"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Grade 5" },
                    { new Guid("b2f7d4a1-8e35-4092-ac16-4d9f3b7e5a28"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الثاني" },
                    { new Guid("c2287d7b-e111-45ef-ac10-a3669c8a5eeb"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Grade 11" },
                    { new Guid("c3a8e5b2-9f46-41a3-bd27-5e0a4c8f6b39"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الثالث" },
                    { new Guid("c3f4e414-cde4-4929-a235-d4f09b5f748d"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Grade 7" },
                    { new Guid("cc203a51-cc8c-4814-ae5e-587c9903e17c"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Grade 2" },
                    { new Guid("d4b9f6c3-a057-42b4-ce38-6f1b5d907c4a"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الرابع" },
                    { new Guid("de6e690a-9bbf-4e32-afea-7e36942f7957"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Grade 8" },
                    { new Guid("e5c0a7d4-b168-43c5-df49-701c6e018d5b"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الخامس" },
                    { new Guid("ee0bc8b5-de4a-4a25-afaa-cfdec9e9e27a"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Grade 1" },
                    { new Guid("f4313c32-260c-4e55-8877-c68b9d5ae33d"), new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "Grade 10" },
                    { new Guid("f6d1b8e5-c279-44d6-e05a-812d7f129e6c"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف السادس" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Terms_GradeId",
                table: "Terms",
                column: "GradeId");

            migrationBuilder.CreateIndex(
                name: "IX_Terms_Lang_Id",
                table: "Terms",
                column: "Lang_Id");

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_Lang_Id",
                table: "Subjects",
                column: "Lang_Id");

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_TermId",
                table: "Subjects",
                column: "TermId");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_LessonId_IsActive",
                table: "Questions",
                columns: new[] { "LessonId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_ChapterId",
                table: "Lessons",
                column: "ChapterId");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_Lang_Id",
                table: "Lessons",
                column: "Lang_Id");

            migrationBuilder.CreateIndex(
                name: "IX_LessonQuestionUploads_LessonId_Version",
                table: "LessonQuestionUploads",
                columns: new[] { "LessonId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Grades_Lang_Id",
                table: "Grades",
                column: "Lang_Id");

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_Lang_Id",
                table: "Chapters",
                column: "Lang_Id");

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_SubjectId",
                table: "Chapters",
                column: "SubjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Chapters_Languages_Lang_Id",
                table: "Chapters",
                column: "Lang_Id",
                principalTable: "Languages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Grades_Languages_Lang_Id",
                table: "Grades",
                column: "Lang_Id",
                principalTable: "Languages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Lessons_Languages_Lang_Id",
                table: "Lessons",
                column: "Lang_Id",
                principalTable: "Languages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Subjects_Languages_Lang_Id",
                table: "Subjects",
                column: "Lang_Id",
                principalTable: "Languages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Terms_Languages_Lang_Id",
                table: "Terms",
                column: "Lang_Id",
                principalTable: "Languages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
