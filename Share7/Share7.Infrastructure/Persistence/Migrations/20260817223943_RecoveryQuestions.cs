using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the secondary per-lesson question pool — <c>recoveryQuestions</c>, which the Unity
    /// models have always named but which had no table until now.
    /// <para>
    /// Structurally a clone of the four question tables: <c>RecoveryQuestions</c> /
    /// <c>RecoveryQuestionChoices</c> hold the content, <c>LessonRecoveryQuestionSets</c> holds
    /// the per-(lesson, language) version, <c>LessonRecoveryQuestionUploads</c> is the audit
    /// trail. Same columns, same foreign keys, same indexes, same cascade behaviour.
    /// </para>
    /// <para>
    /// Separate tables rather than a flag on <c>Questions</c> because the two pools are uploaded
    /// independently and each needs its own version counter — a client caching both compares two
    /// versions. Purely additive: nothing existing is touched, so this is safe on live data.
    /// </para>
    /// </summary>
    public partial class RecoveryQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LessonRecoveryQuestionSets",
                columns: table => new
                {
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Lang_Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonRecoveryQuestionSets", x => new { x.LessonId, x.Lang_Id });
                    table.ForeignKey(
                        name: "FK_LessonRecoveryQuestionSets_Languages_Lang_Id",
                        column: x => x.Lang_Id,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LessonRecoveryQuestionSets_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LessonRecoveryQuestionUploads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Lang_Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    QuestionCount = table.Column<int>(type: "int", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonRecoveryQuestionUploads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonRecoveryQuestionUploads_Languages_Lang_Id",
                        column: x => x.Lang_Id,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LessonRecoveryQuestionUploads_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryQuestions",
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
                    table.PrimaryKey("PK_RecoveryQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecoveryQuestions_Languages_Lang_Id",
                        column: x => x.Lang_Id,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecoveryQuestions_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryQuestionChoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecoveryQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Choice = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OrderIndex = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryQuestionChoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecoveryQuestionChoices_RecoveryQuestions_RecoveryQuestionId",
                        column: x => x.RecoveryQuestionId,
                        principalTable: "RecoveryQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LessonRecoveryQuestionSets_Lang_Id",
                table: "LessonRecoveryQuestionSets",
                column: "Lang_Id");

            migrationBuilder.CreateIndex(
                name: "IX_LessonRecoveryQuestionUploads_Lang_Id",
                table: "LessonRecoveryQuestionUploads",
                column: "Lang_Id");

            migrationBuilder.CreateIndex(
                name: "IX_LessonRecoveryQuestionUploads_LessonId_Lang_Id_Version",
                table: "LessonRecoveryQuestionUploads",
                columns: new[] { "LessonId", "Lang_Id", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryQuestionChoices_RecoveryQuestionId",
                table: "RecoveryQuestionChoices",
                column: "RecoveryQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryQuestions_Lang_Id",
                table: "RecoveryQuestions",
                column: "Lang_Id");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryQuestions_LessonId_Lang_Id_IsActive",
                table: "RecoveryQuestions",
                columns: new[] { "LessonId", "Lang_Id", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LessonRecoveryQuestionSets");

            migrationBuilder.DropTable(
                name: "LessonRecoveryQuestionUploads");

            migrationBuilder.DropTable(
                name: "RecoveryQuestionChoices");

            migrationBuilder.DropTable(
                name: "RecoveryQuestions");
        }
    }
}
