using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MCPhappey.Servers.SQL.Migrations
{
    /// <inheritdoc />
    public partial class Icons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PromptResources");

            migrationBuilder.DropTable(
                name: "PromptResourceTemplates");

            migrationBuilder.AddColumn<string>(
                name: "WebsiteUrl",
                table: "Servers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Icons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MimeType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sizes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Icons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromptIcons",
                columns: table => new
                {
                    PromptId = table.Column<int>(type: "int", nullable: false),
                    IconId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptIcons", x => new { x.PromptId, x.IconId });
                    table.ForeignKey(
                        name: "FK_PromptIcons_Icons_IconId",
                        column: x => x.IconId,
                        principalTable: "Icons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PromptIcons_Prompts_PromptId",
                        column: x => x.PromptId,
                        principalTable: "Prompts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResourceIcons",
                columns: table => new
                {
                    ResourceId = table.Column<int>(type: "int", nullable: false),
                    IconId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceIcons", x => new { x.ResourceId, x.IconId });
                    table.ForeignKey(
                        name: "FK_ResourceIcons_Icons_IconId",
                        column: x => x.IconId,
                        principalTable: "Icons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ResourceIcons_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServerIcons",
                columns: table => new
                {
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    IconId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerIcons", x => new { x.ServerId, x.IconId });
                    table.ForeignKey(
                        name: "FK_ServerIcons_Icons_IconId",
                        column: x => x.IconId,
                        principalTable: "Icons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServerIcons_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PromptIcons_IconId",
                table: "PromptIcons",
                column: "IconId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceIcons_IconId",
                table: "ResourceIcons",
                column: "IconId");

            migrationBuilder.CreateIndex(
                name: "IX_ServerIcons_IconId",
                table: "ServerIcons",
                column: "IconId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PromptIcons");

            migrationBuilder.DropTable(
                name: "ResourceIcons");

            migrationBuilder.DropTable(
                name: "ServerIcons");

            migrationBuilder.DropTable(
                name: "Icons");

            migrationBuilder.DropColumn(
                name: "WebsiteUrl",
                table: "Servers");

            migrationBuilder.CreateTable(
                name: "PromptResources",
                columns: table => new
                {
                    PromptId = table.Column<int>(type: "int", nullable: false),
                    ResourceId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptResources", x => new { x.PromptId, x.ResourceId });
                    table.ForeignKey(
                        name: "FK_PromptResources_Prompts_PromptId",
                        column: x => x.PromptId,
                        principalTable: "Prompts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PromptResources_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PromptResourceTemplates",
                columns: table => new
                {
                    PromptId = table.Column<int>(type: "int", nullable: false),
                    ResourceTemplateId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptResourceTemplates", x => new { x.PromptId, x.ResourceTemplateId });
                    table.ForeignKey(
                        name: "FK_PromptResourceTemplates_Prompts_PromptId",
                        column: x => x.PromptId,
                        principalTable: "Prompts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PromptResourceTemplates_ResourceTemplates_ResourceTemplateId",
                        column: x => x.ResourceTemplateId,
                        principalTable: "ResourceTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PromptResources_ResourceId",
                table: "PromptResources",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_PromptResourceTemplates_ResourceTemplateId",
                table: "PromptResourceTemplates",
                column: "ResourceTemplateId");
        }
    }
}
