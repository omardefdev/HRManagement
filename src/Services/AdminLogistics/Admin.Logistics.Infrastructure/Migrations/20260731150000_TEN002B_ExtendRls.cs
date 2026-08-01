using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using HRManagement.ServiceDefaults;
using Admin.Logistics.Infrastructure.Data;

#nullable disable

namespace Admin.Logistics.Infrastructure.Migrations;

/// <summary>TEN-002B: enable PostgreSQL RLS on all tenant-owned tables added after TEN002.</summary>
[DbContext(typeof(AdminLogisticsDbContext))]
[Migration("20260731150000_TEN002B_ExtendRls")]
public partial class TEN002B_ExtendRls : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        TenantRlsMigrationHelper.EnableRlsOnAllTenantIdTables(migrationBuilder);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally empty: disabling RLS on all tables would widen access. Re-apply TEN002 if needed.
    }
}
