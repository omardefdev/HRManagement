using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Admin.Logistics.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FUN005EXT_Phase5Extensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InventoryItemId",
                table: "ReceiptRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuantityReceived",
                table: "ReceiptRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginReceiptRecordId",
                table: "Assets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HandoverFileId",
                table: "AssetAssignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReturnFileId",
                table: "AssetAssignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "AssetAssignments",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql("""
                UPDATE "AssetAssignments" SET "Status" = 2 WHERE "ReturnedAt" IS NOT NULL;
                UPDATE "AssetAssignments" SET "Status" = 1 WHERE "ReturnedAt" IS NULL;
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowInstanceId",
                table: "AssetAssignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SupplyOrderLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplyOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "text", nullable: true),
                    QtyRequested = table.Column<int>(type: "integer", nullable: false),
                    QtyReceived = table.Column<int>(type: "integer", nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric", nullable: true),
                    IsCapitalizable = table.Column<bool>(type: "boolean", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplyOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplyOrderLines_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SupplyOrderLines_SupplyOrders_SupplyOrderId",
                        column: x => x.SupplyOrderId,
                        principalTable: "SupplyOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptRecords_InventoryItemId",
                table: "ReceiptRecords",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_OriginReceiptRecordId",
                table: "Assets",
                column: "OriginReceiptRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyOrderLines_InventoryItemId",
                table: "SupplyOrderLines",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyOrderLines_SupplyOrderId",
                table: "SupplyOrderLines",
                column: "SupplyOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_ReceiptRecords_OriginReceiptRecordId",
                table: "Assets",
                column: "OriginReceiptRecordId",
                principalTable: "ReceiptRecords",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ReceiptRecords_InventoryItems_InventoryItemId",
                table: "ReceiptRecords",
                column: "InventoryItemId",
                principalTable: "InventoryItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assets_ReceiptRecords_OriginReceiptRecordId",
                table: "Assets");

            migrationBuilder.DropForeignKey(
                name: "FK_ReceiptRecords_InventoryItems_InventoryItemId",
                table: "ReceiptRecords");

            migrationBuilder.DropTable(
                name: "SupplyOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_ReceiptRecords_InventoryItemId",
                table: "ReceiptRecords");

            migrationBuilder.DropIndex(
                name: "IX_Assets_OriginReceiptRecordId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "InventoryItemId",
                table: "ReceiptRecords");

            migrationBuilder.DropColumn(
                name: "QuantityReceived",
                table: "ReceiptRecords");

            migrationBuilder.DropColumn(
                name: "OriginReceiptRecordId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "HandoverFileId",
                table: "AssetAssignments");

            migrationBuilder.DropColumn(
                name: "ReturnFileId",
                table: "AssetAssignments");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "AssetAssignments");

            migrationBuilder.DropColumn(
                name: "WorkflowInstanceId",
                table: "AssetAssignments");
        }
    }
}
