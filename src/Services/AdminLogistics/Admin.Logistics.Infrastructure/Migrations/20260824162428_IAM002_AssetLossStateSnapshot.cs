using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Admin.Logistics.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IAM002_AssetLossStateSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AfterAssetStatus",
                table: "AssetLossReports",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AfterStateJson",
                table: "AssetLossReports",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BeforeAssetStatus",
                table: "AssetLossReports",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BeforeStateJson",
                table: "AssetLossReports",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SettlementApprovedAt",
                table: "AssetLossReports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SettlementApprovedBy",
                table: "AssetLossReports",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmployeeOrgUnitSnapshots",
                columns: table => new
                {
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrimaryOrgUnitId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeOrgUnitSnapshots", x => x.EmployeeId);
                });

            migrationBuilder.CreateTable(
                name: "OrgUnitSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    MaterializedPath = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    NameAr = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgUnitSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeOrgUnitSnapshots_PrimaryOrgUnitId",
                table: "EmployeeOrgUnitSnapshots",
                column: "PrimaryOrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgUnitSnapshots_ParentId",
                table: "OrgUnitSnapshots",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgUnitSnapshots_TenantId_MaterializedPath",
                table: "OrgUnitSnapshots",
                columns: new[] { "TenantId", "MaterializedPath" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeOrgUnitSnapshots");

            migrationBuilder.DropTable(
                name: "OrgUnitSnapshots");

            migrationBuilder.DropColumn(
                name: "AfterAssetStatus",
                table: "AssetLossReports");

            migrationBuilder.DropColumn(
                name: "AfterStateJson",
                table: "AssetLossReports");

            migrationBuilder.DropColumn(
                name: "BeforeAssetStatus",
                table: "AssetLossReports");

            migrationBuilder.DropColumn(
                name: "BeforeStateJson",
                table: "AssetLossReports");

            migrationBuilder.DropColumn(
                name: "SettlementApprovedAt",
                table: "AssetLossReports");

            migrationBuilder.DropColumn(
                name: "SettlementApprovedBy",
                table: "AssetLossReports");
        }
    }
}
