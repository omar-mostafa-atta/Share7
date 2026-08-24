using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Removes scene identity from the game catalogue: <c>LobbyScene</c>, <c>GameplayScene</c>
    /// and the two scene addresses.
    /// <para>
    /// <b>This drops data, and that is the point.</b> The values were client build artifacts — a
    /// Unity build index that a rebuild silently renumbers, and an Addressables address the server
    /// can never resolve because it cannot see the client's content catalogue. Nothing read them:
    /// no backend code outside the game read/write services, and no Unity code at all, which
    /// resolves every scene from <c>MiniGameDefinitionSO</c>. A value no reader consumes and no
    /// writer can validate is a second source of truth waiting to disagree with the first.
    /// </para>
    /// <para>
    /// Scene identity now lives only on the Unity definition, which Addressables delivers
    /// alongside the scenes themselves — so a downloadable mini-game still names its scenes, and
    /// names them in the one place that can actually load them.
    /// </para>
    /// <para>
    /// <see cref="Down"/> restores the columns but not their contents; they are unrecoverable
    /// from here, and re-authoring them would only recreate the disagreement.
    /// </para>
    /// </summary>
    public partial class DropGameSceneColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GameplayScene",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "GameplaySceneAddress",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "LobbyScene",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "LobbySceneAddress",
                table: "Games");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GameplayScene",
                table: "Games",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "GameplaySceneAddress",
                table: "Games",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LobbyScene",
                table: "Games",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LobbySceneAddress",
                table: "Games",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);
        }
    }
}
