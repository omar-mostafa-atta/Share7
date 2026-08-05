using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveLanguageToUserAndDropLearningLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Language selection moved from StudentProfiles.LearningLanguage ("En"/"Ar") to
            // AspNetUsers.PreferredLanguageId. Carry existing choices over before dropping the
            // column, otherwise everyone who picked Arabic would silently fall back to English.
            migrationBuilder.Sql(@"
UPDATE u
    SET u.PreferredLanguageId = CASE p.LearningLanguage
        WHEN 'Ar' THEN '4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71'
        ELSE '9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34'
    END
FROM dbo.AspNetUsers u
INNER JOIN dbo.StudentProfiles p ON p.UserId = u.Id
WHERE u.PreferredLanguageId IS NULL;
");

            migrationBuilder.DropColumn(
                name: "LearningLanguage",
                table: "StudentProfiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LearningLanguage",
                table: "StudentProfiles",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "");
        }
    }
}
