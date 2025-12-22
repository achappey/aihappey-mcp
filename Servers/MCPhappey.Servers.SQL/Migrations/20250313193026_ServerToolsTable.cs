using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MCPhappey.Servers.SQL.Migrations
{
    /// <inheritdoc />
    public partial class ServerToolsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServerApiKey_Servers_ServerId",
                table: "ServerApiKey");

            migrationBuilder.DropForeignKey(
                name: "FK_ServerOwner_Servers_ServerId",
                table: "ServerOwner");

            migrationBuilder.DropForeignKey(
                name: "FK_ServerTool_Servers_ServerId",
                table: "ServerTool");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ServerTool",
                table: "ServerTool");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ServerOwner",
                table: "ServerOwner");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ServerApiKey",
                table: "ServerApiKey");

            migrationBuilder.RenameTable(
                name: "ServerTool",
                newName: "Tools");

            migrationBuilder.RenameTable(
                name: "ServerOwner",
                newName: "ServerOwners");

            migrationBuilder.RenameTable(
                name: "ServerApiKey",
                newName: "ServerApiKeys");

            migrationBuilder.RenameIndex(
                name: "IX_ServerTool_ServerId",
                table: "Tools",
                newName: "IX_Tools_ServerId");

            migrationBuilder.RenameIndex(
                name: "IX_ServerOwner_ServerId",
                table: "ServerOwners",
                newName: "IX_ServerOwners_ServerId");

            migrationBuilder.RenameIndex(
                name: "IX_ServerApiKey_ServerId",
                table: "ServerApiKeys",
                newName: "IX_ServerApiKeys_ServerId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Tools",
                table: "Tools",
                columns: ["Name", "ServerId"]);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ServerOwners",
                table: "ServerOwners",
                columns: ["Id", "ServerId"]);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ServerApiKeys",
                table: "ServerApiKeys",
                columns: ["Id", "ServerId"]);

            migrationBuilder.AddForeignKey(
                name: "FK_ServerApiKeys_Servers_ServerId",
                table: "ServerApiKeys",
                column: "ServerId",
                principalTable: "Servers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ServerOwners_Servers_ServerId",
                table: "ServerOwners",
                column: "ServerId",
                principalTable: "Servers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Tools_Servers_ServerId",
                table: "Tools",
                column: "ServerId",
                principalTable: "Servers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServerApiKeys_Servers_ServerId",
                table: "ServerApiKeys");

            migrationBuilder.DropForeignKey(
                name: "FK_ServerOwners_Servers_ServerId",
                table: "ServerOwners");

            migrationBuilder.DropForeignKey(
                name: "FK_Tools_Servers_ServerId",
                table: "Tools");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Tools",
                table: "Tools");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ServerOwners",
                table: "ServerOwners");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ServerApiKeys",
                table: "ServerApiKeys");

            migrationBuilder.RenameTable(
                name: "Tools",
                newName: "ServerTool");

            migrationBuilder.RenameTable(
                name: "ServerOwners",
                newName: "ServerOwner");

            migrationBuilder.RenameTable(
                name: "ServerApiKeys",
                newName: "ServerApiKey");

            migrationBuilder.RenameIndex(
                name: "IX_Tools_ServerId",
                table: "ServerTool",
                newName: "IX_ServerTool_ServerId");

            migrationBuilder.RenameIndex(
                name: "IX_ServerOwners_ServerId",
                table: "ServerOwner",
                newName: "IX_ServerOwner_ServerId");

            migrationBuilder.RenameIndex(
                name: "IX_ServerApiKeys_ServerId",
                table: "ServerApiKey",
                newName: "IX_ServerApiKey_ServerId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ServerTool",
                table: "ServerTool",
                columns: ["Name", "ServerId"]);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ServerOwner",
                table: "ServerOwner",
                columns: ["Id", "ServerId"]);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ServerApiKey",
                table: "ServerApiKey",
                columns: ["Id", "ServerId"]);

            migrationBuilder.AddForeignKey(
                name: "FK_ServerApiKey_Servers_ServerId",
                table: "ServerApiKey",
                column: "ServerId",
                principalTable: "Servers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ServerOwner_Servers_ServerId",
                table: "ServerOwner",
                column: "ServerId",
                principalTable: "Servers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ServerTool_Servers_ServerId",
                table: "ServerTool",
                column: "ServerId",
                principalTable: "Servers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
