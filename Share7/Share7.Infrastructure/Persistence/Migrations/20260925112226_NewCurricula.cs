using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NewCurricula : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CurriculumNodeKindTranslations",
                columns: table => new
                {
                    NodeKindId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LangId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurriculumNodeKindTranslations", x => new { x.NodeKindId, x.LangId });
                    table.ForeignKey(
                        name: "FK_CurriculumNodeKindTranslations_CurriculumNodeKinds_NodeKindId",
                        column: x => x.NodeKindId,
                        principalTable: "CurriculumNodeKinds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CurriculumNodeKindTranslations_Languages_LangId",
                        column: x => x.LangId,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NodeItemRenderings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LangId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CorrectChoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeactivatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeItemRenderings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NodeItemRenderings_CurriculumNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "CurriculumNodes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_NodeItemRenderings_ItemVersions_ItemVersionId",
                        column: x => x.ItemVersionId,
                        principalTable: "ItemVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NodeItemRenderings_Languages_LangId",
                        column: x => x.LangId,
                        principalTable: "Languages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NodeItemRenderingChoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RenderingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OrderIndex = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeItemRenderingChoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NodeItemRenderingChoices_NodeItemRenderings_RenderingId",
                        column: x => x.RenderingId,
                        principalTable: "NodeItemRenderings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CurriculumNodeKindTranslations_LangId",
                table: "CurriculumNodeKindTranslations",
                column: "LangId");

            migrationBuilder.CreateIndex(
                name: "IX_NodeItemRenderingChoices_RenderingId",
                table: "NodeItemRenderingChoices",
                column: "RenderingId");

            migrationBuilder.CreateIndex(
                name: "IX_NodeItemRenderings_ItemVersionId_LangId",
                table: "NodeItemRenderings",
                columns: new[] { "ItemVersionId", "LangId" });

            migrationBuilder.CreateIndex(
                name: "IX_NodeItemRenderings_LangId",
                table: "NodeItemRenderings",
                column: "LangId");

            migrationBuilder.CreateIndex(
                name: "IX_NodeItemRenderings_NodeId_Role_LangId_IsActive",
                table: "NodeItemRenderings",
                columns: new[] { "NodeId", "Role", "LangId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CurriculumNodeKindTranslations");

            migrationBuilder.DropTable(
                name: "NodeItemRenderingChoices");

            migrationBuilder.DropTable(
                name: "NodeItemRenderings");
        }
    }
}
