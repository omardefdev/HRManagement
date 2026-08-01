using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations;

[DbContext(typeof(Data.HRCoreDbContext))]
[Migration("20260728190000_PERF003_OrgUnitPathIndexes")]
public partial class PERF003_OrgUnitPathIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_OrgUnits_MaterializedPath",
            table: "OrgUnits",
            column: "MaterializedPath");

        migrationBuilder.CreateIndex(
            name: "IX_OrgUnits_TenantId_MaterializedPath",
            table: "OrgUnits",
            columns: new[] { "TenantId", "MaterializedPath" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_OrgUnits_TenantId_MaterializedPath",
            table: "OrgUnits");

        migrationBuilder.DropIndex(
            name: "IX_OrgUnits_MaterializedPath",
            table: "OrgUnits");
    }
}
