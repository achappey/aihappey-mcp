using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MCPhappey.Telemetry.Migrations
{
    /// <inheritdoc />
    public partial class Clients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Url",
                table: "Servers",
                newName: "Name");

            migrationBuilder.RenameIndex(
                name: "IX_Servers_Url",
                table: "Servers",
                newName: "IX_Servers_Name");

            migrationBuilder.AddColumn<int>(
                name: "ClientId",
                table: "Request",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SessionId",
                table: "Request",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientName = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClientVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClientId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientVersions_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Request_ClientId",
                table: "Request",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_ClientName",
                table: "Clients",
                column: "ClientName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientVersions_ClientId",
                table: "ClientVersions",
                column: "ClientId");

            migrationBuilder.AddForeignKey(
                name: "FK_Request_Clients_ClientId",
                table: "Request",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Request_Clients_ClientId",
                table: "Request");

            migrationBuilder.DropTable(
                name: "ClientVersions");

            migrationBuilder.DropTable(
                name: "Clients");

            migrationBuilder.DropIndex(
                name: "IX_Request_ClientId",
                table: "Request");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "Request");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "Request");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "Servers",
                newName: "Url");

            migrationBuilder.RenameIndex(
                name: "IX_Servers_Name",
                table: "Servers",
                newName: "IX_Servers_Url");
        }
    }
}
