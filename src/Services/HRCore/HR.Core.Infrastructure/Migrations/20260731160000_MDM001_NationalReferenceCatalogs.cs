using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using HRManagement.ServiceDefaults;
using HR.Core.Infrastructure.Data;

#nullable disable

namespace HR.Core.Infrastructure.Migrations;

/// <summary>MDM-001: national reference catalog table + FK columns on EmploymentRecords / CareerPathSteps.</summary>
[DbContext(typeof(HRCoreDbContext))]
[Migration("20260731160000_MDM001_NationalReferenceCatalogs")]
public partial class MDM001_NationalReferenceCatalogs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ReferenceCatalogEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                TenantId = table.Column<Guid>(nullable: false),
                CreatedAt = table.Column<DateTime>(nullable: false),
                UpdatedAt = table.Column<DateTime>(nullable: false),
                CreatedBy = table.Column<Guid>(nullable: true),
                UpdatedBy = table.Column<Guid>(nullable: true),
                CatalogKind = table.Column<string>(maxLength: 50, nullable: false),
                Code = table.Column<string>(maxLength: 50, nullable: false),
                NameAr = table.Column<string>(maxLength: 300, nullable: false),
                NameEn = table.Column<string>(maxLength: 300, nullable: true),
                NameFr = table.Column<string>(maxLength: 300, nullable: true),
                ValidFrom = table.Column<DateTime>(nullable: true),
                ValidTo = table.Column<DateTime>(nullable: true),
                ReplacedByCode = table.Column<string>(maxLength: 50, nullable: true),
                SourceAuthority = table.Column<string>(maxLength: 200, nullable: true),
                StewardRole = table.Column<string>(maxLength: 100, nullable: true),
                IsActive = table.Column<bool>(nullable: false, defaultValue: true),
                Version = table.Column<int>(nullable: false, defaultValue: 1)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReferenceCatalogEntries", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RefCatalog_Tenant_Kind_Code",
            table: "ReferenceCatalogEntries",
            columns: new[] { "TenantId", "CatalogKind", "Code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_RefCatalog_Kind_Active",
            table: "ReferenceCatalogEntries",
            columns: new[] { "CatalogKind", "IsActive" });

        // MDM-001: add nullable FK columns to existing tables
        migrationBuilder.AddColumn<string>(
            name: "JobClassificationCode",
            table: "EmploymentRecords",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "JobClassificationCode",
            table: "CareerPathSteps",
            maxLength: 50,
            nullable: true);

        // RLS for the new tenant-owned table
        TenantRlsMigrationHelper.EnableRls(migrationBuilder, "ReferenceCatalogEntries");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "JobClassificationCode", table: "CareerPathSteps");
        migrationBuilder.DropColumn(name: "JobClassificationCode", table: "EmploymentRecords");
        migrationBuilder.DropTable(name: "ReferenceCatalogEntries");
    }
}
