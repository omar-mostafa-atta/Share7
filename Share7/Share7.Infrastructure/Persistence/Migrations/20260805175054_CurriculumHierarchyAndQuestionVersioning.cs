using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CurriculumHierarchyAndQuestionVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Grades was seeded twice historically: first imperatively in Program.cs with
            // Guid.NewGuid(), later by the SeedGradeLookupData migration with fixed ids. Any
            // database that lived through both holds 24 rows — 12 duplicate pairs. Collapse
            // them here, before the language rewrite, because rows outside the fixed-id set
            // would otherwise keep a placeholder Lang_Id and fail the new foreign key.
            // Runs before the [Order] column is dropped, since that is what pairs them up.
            migrationBuilder.Sql(@"
DECLARE @Seeded TABLE (Id uniqueidentifier PRIMARY KEY, Ord int);
INSERT INTO @Seeded (Id, Ord) VALUES
    ('ee0bc8b5-de4a-4a25-afaa-cfdec9e9e27a', 1),
    ('cc203a51-cc8c-4814-ae5e-587c9903e17c', 2),
    ('52c97299-9356-46d5-87e9-fc1ac17d5c14', 3),
    ('2b004d42-ca9c-4823-ac5b-1cb59cba0467', 4),
    ('b14d62e5-56ee-42a4-916a-5554d811f72f', 5),
    ('76e10c16-da74-4731-8818-448262946b70', 6),
    ('c3f4e414-cde4-4929-a235-d4f09b5f748d', 7),
    ('de6e690a-9bbf-4e32-afea-7e36942f7957', 8),
    ('895f300d-21b4-4167-afc7-6c06677abf0e', 9),
    ('f4313c32-260c-4e55-8877-c68b9d5ae33d', 10),
    ('c2287d7b-e111-45ef-ac10-a3669c8a5eeb', 11),
    ('38111b66-de42-4adc-b20c-464b1dd2a9d1', 12);

-- Students sitting on a duplicate row move to the canonical row for the same grade.
UPDATE p
    SET p.GradeId = s.Id
FROM dbo.StudentProfiles p
INNER JOIN dbo.Grades dup ON dup.Id = p.GradeId
INNER JOIN @Seeded s ON s.Ord = dup.[Order]
WHERE dup.Id NOT IN (SELECT Id FROM @Seeded);

-- Then drop the duplicates, skipping any that something still references.
DELETE g
FROM dbo.Grades g
WHERE g.Id NOT IN (SELECT Id FROM @Seeded)
  AND NOT EXISTS (SELECT 1 FROM dbo.StudentProfiles p WHERE p.GradeId = g.Id);
");

            migrationBuilder.DropColumn(
                name: "NameAr",
                table: "Grades");

            migrationBuilder.DropColumn(
                name: "Order",
                table: "Grades");

            migrationBuilder.RenameColumn(
                name: "NameEn",
                table: "Grades",
                newName: "Grade");

            migrationBuilder.AddColumn<Guid>(
                name: "Lang_Id",
                table: "Grades",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "PreferredLanguageId",
                table: "AspNetUsers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Languages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Languages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Terms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Term = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Lang_Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GradeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Terms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Terms_Grades_GradeId",
                        column: x => x.GradeId,
                        principalTable: "Grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Terms_Languages_Lang_Id",
                        column: x => x.Lang_Id,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Subjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Lang_Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subjects_Languages_Lang_Id",
                        column: x => x.Lang_Id,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Subjects_Terms_TermId",
                        column: x => x.TermId,
                        principalTable: "Terms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Chapters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Chapter = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Lang_Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chapters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Chapters_Languages_Lang_Id",
                        column: x => x.Lang_Id,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Chapters_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Lessons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Lesson = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Lang_Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChapterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionsVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lessons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Lessons_Chapters_ChapterId",
                        column: x => x.ChapterId,
                        principalTable: "Chapters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Lessons_Languages_Lang_Id",
                        column: x => x.Lang_Id,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LessonQuestionUploads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    QuestionCount = table.Column<int>(type: "int", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonQuestionUploads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonQuestionUploads_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Questions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Lang_Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Question = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CorrectChoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeactivatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Questions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Questions_Languages_Lang_Id",
                        column: x => x.Lang_Id,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Questions_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuestionChoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Choice = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OrderIndex = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionChoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionChoices_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("2b004d42-ca9c-4823-ac5b-1cb59cba0467"),
                column: "Lang_Id",
                value: new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"));

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("38111b66-de42-4adc-b20c-464b1dd2a9d1"),
                column: "Lang_Id",
                value: new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"));

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("52c97299-9356-46d5-87e9-fc1ac17d5c14"),
                column: "Lang_Id",
                value: new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"));

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("76e10c16-da74-4731-8818-448262946b70"),
                column: "Lang_Id",
                value: new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"));

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("895f300d-21b4-4167-afc7-6c06677abf0e"),
                column: "Lang_Id",
                value: new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"));

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("b14d62e5-56ee-42a4-916a-5554d811f72f"),
                column: "Lang_Id",
                value: new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"));

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("c2287d7b-e111-45ef-ac10-a3669c8a5eeb"),
                column: "Lang_Id",
                value: new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"));

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("c3f4e414-cde4-4929-a235-d4f09b5f748d"),
                column: "Lang_Id",
                value: new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"));

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("cc203a51-cc8c-4814-ae5e-587c9903e17c"),
                column: "Lang_Id",
                value: new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"));

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("de6e690a-9bbf-4e32-afea-7e36942f7957"),
                column: "Lang_Id",
                value: new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"));

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("ee0bc8b5-de4a-4a25-afaa-cfdec9e9e27a"),
                column: "Lang_Id",
                value: new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"));

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("f4313c32-260c-4e55-8877-c68b9d5ae33d"),
                column: "Lang_Id",
                value: new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"));

            migrationBuilder.InsertData(
                table: "Languages",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[,]
                {
                    { new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "ar", "العربية" },
                    { new Guid("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34"), "en", "English" }
                });

            migrationBuilder.InsertData(
                table: "Grades",
                columns: new[] { "Id", "Lang_Id", "Grade" },
                values: new object[,]
                {
                    { new Guid("07e2c9f6-d38a-45e7-f16b-923e80230f7d"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف السابع" },
                    { new Guid("18f3d007-e49b-46f8-021c-a34f9134108e"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الثامن" },
                    { new Guid("2904e118-f5ac-4709-132d-b450a245219f"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف التاسع" },
                    { new Guid("3a15f229-06bd-481a-243e-c561b35632a0"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف العاشر" },
                    { new Guid("4b260330-17ce-492b-354f-d672c46743b1"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الحادي عشر" },
                    { new Guid("5c371441-28df-4a3c-4650-e783d57854c2"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الثاني عشر" },
                    { new Guid("a1e6c390-7d24-4f81-9b05-3c8e2a6f4d17"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الأول" },
                    { new Guid("b2f7d4a1-8e35-4092-ac16-4d9f3b7e5a28"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الثاني" },
                    { new Guid("c3a8e5b2-9f46-41a3-bd27-5e0a4c8f6b39"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الثالث" },
                    { new Guid("d4b9f6c3-a057-42b4-ce38-6f1b5d907c4a"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الرابع" },
                    { new Guid("e5c0a7d4-b168-43c5-df49-701c6e018d5b"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف الخامس" },
                    { new Guid("f6d1b8e5-c279-44d6-e05a-812d7f129e6c"), new Guid("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71"), "الصف السادس" }
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_Languages_Code",
                table: "Languages",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LessonQuestionUploads_LessonId_Version",
                table: "LessonQuestionUploads",
                columns: new[] { "LessonId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_ChapterId",
                table: "Lessons",
                column: "ChapterId");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_Lang_Id",
                table: "Lessons",
                column: "Lang_Id");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionChoices_QuestionId",
                table: "QuestionChoices",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_Lang_Id",
                table: "Questions",
                column: "Lang_Id");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_LessonId_IsActive",
                table: "Questions",
                columns: new[] { "LessonId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_Lang_Id",
                table: "Subjects",
                column: "Lang_Id");

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_TermId",
                table: "Subjects",
                column: "TermId");

            migrationBuilder.CreateIndex(
                name: "IX_Terms_GradeId",
                table: "Terms",
                column: "GradeId");

            migrationBuilder.CreateIndex(
                name: "IX_Terms_Lang_Id",
                table: "Terms",
                column: "Lang_Id");

            // Backstop: any grade row the fixed-id updates above did not cover (a hand-added
            // row, or a duplicate still referenced by a student) still holds the placeholder
            // Lang_Id and would fail the foreign key. Treat those as English rather than
            // letting the migration abort.
            migrationBuilder.Sql(@"
UPDATE dbo.Grades
    SET Lang_Id = '9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34'
WHERE Lang_Id = '00000000-0000-0000-0000-000000000000';
");

            migrationBuilder.AddForeignKey(
                name: "FK_Grades_Languages_Lang_Id",
                table: "Grades",
                column: "Lang_Id",
                principalTable: "Languages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Grades_Languages_Lang_Id",
                table: "Grades");

            migrationBuilder.DropTable(
                name: "LessonQuestionUploads");

            migrationBuilder.DropTable(
                name: "QuestionChoices");

            migrationBuilder.DropTable(
                name: "Questions");

            migrationBuilder.DropTable(
                name: "Lessons");

            migrationBuilder.DropTable(
                name: "Chapters");

            migrationBuilder.DropTable(
                name: "Subjects");

            migrationBuilder.DropTable(
                name: "Terms");

            migrationBuilder.DropTable(
                name: "Languages");

            migrationBuilder.DropIndex(
                name: "IX_Grades_Lang_Id",
                table: "Grades");

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
                keyValue: new Guid("3a15f229-06bd-481a-243e-c561b35632a0"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("4b260330-17ce-492b-354f-d672c46743b1"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("5c371441-28df-4a3c-4650-e783d57854c2"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("a1e6c390-7d24-4f81-9b05-3c8e2a6f4d17"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("b2f7d4a1-8e35-4092-ac16-4d9f3b7e5a28"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("c3a8e5b2-9f46-41a3-bd27-5e0a4c8f6b39"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("d4b9f6c3-a057-42b4-ce38-6f1b5d907c4a"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("e5c0a7d4-b168-43c5-df49-701c6e018d5b"));

            migrationBuilder.DeleteData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("f6d1b8e5-c279-44d6-e05a-812d7f129e6c"));

            migrationBuilder.DropColumn(
                name: "Lang_Id",
                table: "Grades");

            migrationBuilder.DropColumn(
                name: "PreferredLanguageId",
                table: "AspNetUsers");

            migrationBuilder.RenameColumn(
                name: "Grade",
                table: "Grades",
                newName: "NameEn");

            migrationBuilder.AddColumn<string>(
                name: "NameAr",
                table: "Grades",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Order",
                table: "Grades",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("2b004d42-ca9c-4823-ac5b-1cb59cba0467"),
                columns: new[] { "NameAr", "Order" },
                values: new object[] { "الصف الرابع", 4 });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("38111b66-de42-4adc-b20c-464b1dd2a9d1"),
                columns: new[] { "NameAr", "Order" },
                values: new object[] { "الصف الثاني عشر", 12 });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("52c97299-9356-46d5-87e9-fc1ac17d5c14"),
                columns: new[] { "NameAr", "Order" },
                values: new object[] { "الصف الثالث", 3 });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("76e10c16-da74-4731-8818-448262946b70"),
                columns: new[] { "NameAr", "Order" },
                values: new object[] { "الصف السادس", 6 });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("895f300d-21b4-4167-afc7-6c06677abf0e"),
                columns: new[] { "NameAr", "Order" },
                values: new object[] { "الصف التاسع", 9 });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("b14d62e5-56ee-42a4-916a-5554d811f72f"),
                columns: new[] { "NameAr", "Order" },
                values: new object[] { "الصف الخامس", 5 });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("c2287d7b-e111-45ef-ac10-a3669c8a5eeb"),
                columns: new[] { "NameAr", "Order" },
                values: new object[] { "الصف الحادي عشر", 11 });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("c3f4e414-cde4-4929-a235-d4f09b5f748d"),
                columns: new[] { "NameAr", "Order" },
                values: new object[] { "الصف السابع", 7 });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("cc203a51-cc8c-4814-ae5e-587c9903e17c"),
                columns: new[] { "NameAr", "Order" },
                values: new object[] { "الصف الثاني", 2 });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("de6e690a-9bbf-4e32-afea-7e36942f7957"),
                columns: new[] { "NameAr", "Order" },
                values: new object[] { "الصف الثامن", 8 });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("ee0bc8b5-de4a-4a25-afaa-cfdec9e9e27a"),
                columns: new[] { "NameAr", "Order" },
                values: new object[] { "الصف الأول", 1 });

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Id",
                keyValue: new Guid("f4313c32-260c-4e55-8877-c68b9d5ae33d"),
                columns: new[] { "NameAr", "Order" },
                values: new object[] { "الصف العاشر", 10 });
        }
    }
}
