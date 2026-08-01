using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IT.Infra.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase6_HostnameUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ServerDevices_Hostname",
                table: "ServerDevices",
                column: "Hostname",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ServerDevices_Hostname",
                table: "ServerDevices");
        }
    }
}
