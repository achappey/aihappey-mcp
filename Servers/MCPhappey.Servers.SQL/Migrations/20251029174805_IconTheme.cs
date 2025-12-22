using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MCPhappey.Servers.SQL.Migrations
{
    /// <inheritdoc />
    public partial class IconTheme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Theme",
                table: "Icons",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Theme",
                table: "Icons");
        }
    }
}
