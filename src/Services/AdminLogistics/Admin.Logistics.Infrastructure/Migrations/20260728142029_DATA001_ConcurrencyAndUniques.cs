using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Admin.Logistics.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DATA001_ConcurrencyAndUniques : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "InventoryItems",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "InventoryItems");
        }
    }
}
