using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Admin.Logistics.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DATA001_SyncModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryItems_Sku",
                table: "InventoryItems");

            migrationBuilder.DropIndex(
                name: "IX_Assets_TagNumber",
                table: "Assets");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "AssetAssignments",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_TenantId_Sku",
                table: "InventoryItems",
                columns: new[] { "TenantId", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_TenantId_TagNumber",
                table: "Assets",
                columns: new[] { "TenantId", "TagNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryItems_TenantId_Sku",
                table: "InventoryItems");

            migrationBuilder.DropIndex(
                name: "IX_Assets_TenantId_TagNumber",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "AssetAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_Sku",
                table: "InventoryItems",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_TagNumber",
                table: "Assets",
                column: "TagNumber",
                unique: true);
        }
    }
}
