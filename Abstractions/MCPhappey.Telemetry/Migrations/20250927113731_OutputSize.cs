using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MCPhappey.Telemetry.Migrations
{
    /// <inheritdoc />
    public partial class OutputSize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ToolRequest_OutputSize",
                table: "Request");

            migrationBuilder.AlterColumn<int>(
                name: "OutputSize",
                table: "Request",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "OutputSize",
                table: "Request",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "ToolRequest_OutputSize",
                table: "Request",
                type: "int",
                nullable: true);
        }
    }
}
