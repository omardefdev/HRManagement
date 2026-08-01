using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Payroll.Infrastructure.Data;

#nullable disable

namespace Payroll.Infrastructure.Migrations;

[DbContext(typeof(PayrollDbContext))]
[Migration("20260728190001_PERF003_OrgUnitSnapshotPathIndexes")]
public partial class PERF003_OrgUnitSnapshotPathIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_OrgUnitSnapshots_ParentId",
            table: "OrgUnitSnapshots",
            column: "ParentId");

        migrationBuilder.CreateIndex(
            name: "IX_OrgUnitSnapshots_TenantId_MaterializedPath",
            table: "OrgUnitSnapshots",
            columns: new[] { "TenantId", "MaterializedPath" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_OrgUnitSnapshots_TenantId_MaterializedPath",
            table: "OrgUnitSnapshots");

        migrationBuilder.DropIndex(
            name: "IX_OrgUnitSnapshots_ParentId",
            table: "OrgUnitSnapshots");
    }
}
