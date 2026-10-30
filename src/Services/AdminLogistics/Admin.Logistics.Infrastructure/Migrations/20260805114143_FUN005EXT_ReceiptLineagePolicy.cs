using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Admin.Logistics.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FUN005EXT_ReceiptLineagePolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLegacyEntry",
                table: "Assets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LegacyEntryReason",
                table: "Assets",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginSupplyOrderLineId",
                table: "Assets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_OriginSupplyOrderLineId",
                table: "Assets",
                column: "OriginSupplyOrderLineId");

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_SupplyOrderLines_OriginSupplyOrderLineId",
                table: "Assets",
                column: "OriginSupplyOrderLineId",
                principalTable: "SupplyOrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assets_SupplyOrderLines_OriginSupplyOrderLineId",
                table: "Assets");

            migrationBuilder.DropIndex(
                name: "IX_Assets_OriginSupplyOrderLineId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "IsLegacyEntry",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "LegacyEntryReason",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "OriginSupplyOrderLineId",
                table: "Assets");
        }
    }
}
