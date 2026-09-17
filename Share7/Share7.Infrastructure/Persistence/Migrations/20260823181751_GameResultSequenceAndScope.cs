using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GameResultSequenceAndScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_GameResult_Submission",
                table: "GameResults");

            migrationBuilder.AddColumn<string>(
                name: "Scope",
                table: "GameResults",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Sequence",
                table: "GameResults",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.CreateIndex(
                name: "UX_GameResult_Sequence",
                table: "GameResults",
                column: "Sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_GameResult_Submission",
                table: "GameResults",
                columns: new[] { "UserId", "RequestId", "Metric", "Scope" },
                unique: true,
                filter: "[RequestId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_GameResult_Sequence",
                table: "GameResults");

            migrationBuilder.DropIndex(
                name: "UX_GameResult_Submission",
                table: "GameResults");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "GameResults");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "GameResults");

            migrationBuilder.CreateIndex(
                name: "UX_GameResult_Submission",
                table: "GameResults",
                columns: new[] { "UserId", "RequestId", "Metric" },
                unique: true,
                filter: "[RequestId] IS NOT NULL");
        }
    }
}
