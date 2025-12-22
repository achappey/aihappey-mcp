using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MCPhappey.Servers.SQL.Migrations
{
    /// <inheritdoc />
    public partial class OutputTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ToolPrompts",
                table: "Servers",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MimeType",
                table: "Resources",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ToolMetadata",
                columns: table => new
                {
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    ToolName = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OutputTemplate = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolMetadata", x => new { x.ServerId, x.ToolName });
                    table.ForeignKey(
                        name: "FK_ToolMetadata_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ToolMetadata");

            migrationBuilder.DropColumn(
                name: "ToolPrompts",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "MimeType",
                table: "Resources");
        }
    }
}
