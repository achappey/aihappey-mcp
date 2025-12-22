using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MCPhappey.Servers.SQL.Migrations
{
    /// <inheritdoc />
    public partial class Annotations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Servers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AssistantAudience",
                table: "ResourceTemplates",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "ResourceTemplates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UserAudience",
                table: "ResourceTemplates",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AssistantAudience",
                table: "Resources",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "Resources",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UserAudience",
                table: "Resources",
                type: "bit",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Title",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "AssistantAudience",
                table: "ResourceTemplates");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "ResourceTemplates");

            migrationBuilder.DropColumn(
                name: "UserAudience",
                table: "ResourceTemplates");

            migrationBuilder.DropColumn(
                name: "AssistantAudience",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "UserAudience",
                table: "Resources");
        }
    }
}
