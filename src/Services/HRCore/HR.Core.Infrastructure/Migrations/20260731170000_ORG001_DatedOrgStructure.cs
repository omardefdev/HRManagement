using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using HRManagement.ServiceDefaults;
using HR.Core.Infrastructure.Data;

#nullable disable

namespace HR.Core.Infrastructure.Migrations;

/// <summary>ORG-001: dated org-unit version table for temporal org structure.</summary>
[DbContext(typeof(HRCoreDbContext))]
[Migration("20260731170000_ORG001_DatedOrgStructure")]
public partial class ORG001_DatedOrgStructure : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "OrgUnitVersions",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                TenantId = table.Column<Guid>(nullable: false),
                CreatedAt = table.Column<DateTime>(nullable: false),
                UpdatedAt = table.Column<DateTime>(nullable: false),
                CreatedBy = table.Column<Guid>(nullable: true),
                UpdatedBy = table.Column<Guid>(nullable: true),
                OrgUnitId = table.Column<Guid>(nullable: false),
                ValidFrom = table.Column<DateTime>(nullable: false),
                ValidTo = table.Column<DateTime>(nullable: true),
                Name = table.Column<string>(maxLength: 200, nullable: false),
                NameEn = table.Column<string>(maxLength: 200, nullable: true),
                ParentId = table.Column<Guid>(nullable: true),
                OperationType = table.Column<string>(maxLength: 32, nullable: false),
                ApprovedBy = table.Column<string>(maxLength: 200, nullable: true),
                DecisionReference = table.Column<string>(maxLength: 100, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrgUnitVersions", x => x.Id);
                table.ForeignKey(
                    name: "FK_OrgUnitVersions_OrgUnits_OrgUnitId",
                    column: x => x.OrgUnitId,
                    principalTable: "OrgUnits",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_OrgUnitVersions_OrgUnitId_ValidFrom",
            table: "OrgUnitVersions",
            columns: new[] { "OrgUnitId", "ValidFrom" });

        migrationBuilder.CreateIndex(
            name: "IX_OrgUnitVersions_TenantId_OrgUnitId_ValidFrom",
            table: "OrgUnitVersions",
            columns: new[] { "TenantId", "OrgUnitId", "ValidFrom" });

        TenantRlsMigrationHelper.EnableRls(migrationBuilder, "OrgUnitVersions");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "OrgUnitVersions");
    }
}
