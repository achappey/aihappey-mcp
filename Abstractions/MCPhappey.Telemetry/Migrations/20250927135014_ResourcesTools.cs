using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MCPhappey.Telemetry.Migrations
{
    /// <inheritdoc />
    public partial class ResourcesTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ToolName",
                table: "Request");

            migrationBuilder.DropColumn(
                name: "Uri",
                table: "Request");

            migrationBuilder.AddColumn<int>(
                name: "ResourceId",
                table: "Request",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ToolId",
                table: "Request",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Resources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Uri = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Resources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tools",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ToolName = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tools", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Request_ResourceId",
                table: "Request",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_Request_ToolId",
                table: "Request",
                column: "ToolId");

            migrationBuilder.CreateIndex(
                name: "IX_Resources_Uri",
                table: "Resources",
                column: "Uri",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tools_ToolName",
                table: "Tools",
                column: "ToolName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Request_Resources_ResourceId",
                table: "Request",
                column: "ResourceId",
                principalTable: "Resources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Request_Tools_ToolId",
                table: "Request",
                column: "ToolId",
                principalTable: "Tools",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Request_Resources_ResourceId",
                table: "Request");

            migrationBuilder.DropForeignKey(
                name: "FK_Request_Tools_ToolId",
                table: "Request");

            migrationBuilder.DropTable(
                name: "Resources");

            migrationBuilder.DropTable(
                name: "Tools");

            migrationBuilder.DropIndex(
                name: "IX_Request_ResourceId",
                table: "Request");

            migrationBuilder.DropIndex(
                name: "IX_Request_ToolId",
                table: "Request");

            migrationBuilder.DropColumn(
                name: "ResourceId",
                table: "Request");

            migrationBuilder.DropColumn(
                name: "ToolId",
                table: "Request");

            migrationBuilder.AddColumn<string>(
                name: "ToolName",
                table: "Request",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Uri",
                table: "Request",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
