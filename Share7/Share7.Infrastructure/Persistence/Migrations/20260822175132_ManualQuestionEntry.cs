using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Records how a published question version was authored, now that hand entry exists alongside
    /// the Excel upload.
    /// <para>
    /// <b>The default value is the whole point and is hand-set.</b> EF scaffolded
    /// <c>defaultValue: ""</c>, which would stamp every historical row with a blank source — and a
    /// blank reads as missing data rather than as a known one, which is exactly what this column
    /// was added to stop. Every row that already exists was published from a sheet, because until
    /// this migration there was no other way, so <c>EXCEL_UPLOAD</c> is not a guess: it is the
    /// only thing those rows can be.
    /// </para>
    /// </summary>
    public partial class ManualQuestionEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "LessonRecoveryQuestionUploads",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "EXCEL_UPLOAD");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "LessonQuestionUploads",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "EXCEL_UPLOAD");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Source",
                table: "LessonRecoveryQuestionUploads");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "LessonQuestionUploads");
        }
    }
}
