using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Admin.Logistics.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase5_SkuUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_Sku",
                table: "InventoryItems",
                column: "Sku",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryItems_Sku",
                table: "InventoryItems");
        }
    }
}
