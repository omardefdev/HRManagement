using HR.Contracts.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Contracts.Infrastructure.Migrations;

[DbContext(typeof(ContractsDbContext))]
[Migration("20260728190002_PERF003_OrgUnitSnapshotPathIndexes")]
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
