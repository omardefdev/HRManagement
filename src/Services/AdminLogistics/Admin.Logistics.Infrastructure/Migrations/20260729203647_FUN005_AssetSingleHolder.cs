using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Admin.Logistics.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FUN005_AssetSingleHolder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AssetAssignments_AssetId",
                table: "AssetAssignments");

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Assets",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "Assets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCancelled",
                table: "Assets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPrivileged",
                table: "Assets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceRef",
                table: "AssetAssignments",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_AssetId",
                table: "AssetAssignments",
                column: "AssetId",
                unique: true,
                filter: "\"ReturnedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AssetAssignments_AssetId",
                table: "AssetAssignments");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "IsCancelled",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "IsPrivileged",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "EvidenceRef",
                table: "AssetAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_AssetId",
                table: "AssetAssignments",
                column: "AssetId");
        }
    }
}
