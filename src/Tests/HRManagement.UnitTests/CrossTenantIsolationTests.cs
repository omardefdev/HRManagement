using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.UnitTests;

public class CrossTenantIsolationTests
{
    private static ITenantContext FixedTenant(Guid tenantId, bool platformAdmin = false)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        mock.Setup(t => t.IsPlatformOperator).Returns(platformAdmin);
        mock.Setup(t => t.RequireTenantId()).Returns(tenantId);
        return mock.Object;
    }

    private static HRCoreDbContext CreateDb(ITenantContext tenant, string? name = null)
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(name ?? $"tenant-{Guid.NewGuid()}")
            .AddInterceptors(new TenantIdStampInterceptor(tenant))
            .Options;
        return new HRCoreDbContext(options, tenant);
    }

    [Fact]
    public async Task Query_filter_returns_only_current_tenant_rows()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var dbName = $"iso-{Guid.NewGuid()}";

        await using (var seedA = CreateDb(FixedTenant(tenantA), dbName))
        {
            seedA.Employees.Add(new Employee
            {
                TenantId = tenantA,
                FullName = "A",
                NationalId = $"A-{Guid.NewGuid():N}"[..20]
            });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateDb(FixedTenant(tenantB), dbName))
        {
            seedB.Employees.Add(new Employee
            {
                TenantId = tenantB,
                FullName = "B",
                NationalId = $"B-{Guid.NewGuid():N}"[..20]
            });
            await seedB.SaveChangesAsync();
        }

        await using var readA = CreateDb(FixedTenant(tenantA), dbName);
        var namesA = await readA.Employees.Select(e => e.FullName).ToListAsync();
        Assert.Equal(["A"], namesA);

        await using var readB = CreateDb(FixedTenant(tenantB), dbName);
        var namesB = await readB.Employees.Select(e => e.FullName).ToListAsync();
        Assert.Equal(["B"], namesB);
    }

    [Fact]
    public async Task Insert_without_tenant_is_rejected()
    {
        var empty = new Mock<ITenantContext>();
        empty.Setup(t => t.TenantId).Returns((Guid?)null);
        empty.Setup(t => t.IsPlatformOperator).Returns(false);

        await using var db = CreateDb(empty.Object);
        db.Employees.Add(new Employee { FullName = "X", NationalId = "123" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Platform_admin_bypass_sees_all_tenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var dbName = $"bypass-{Guid.NewGuid()}";

        await using (var seedA = CreateDb(FixedTenant(tenantA), dbName))
        {
            seedA.Employees.Add(new Employee
            {
                TenantId = tenantA,
                FullName = "A",
                NationalId = $"A-{Guid.NewGuid():N}"[..20]
            });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateDb(FixedTenant(tenantB), dbName))
        {
            seedB.Employees.Add(new Employee
            {
                TenantId = tenantB,
                FullName = "B",
                NationalId = $"B-{Guid.NewGuid():N}"[..20]
            });
            await seedB.SaveChangesAsync();
        }

        var admin = FixedTenant(tenantA, platformAdmin: true);
        await using var db = CreateDb(admin, dbName);
        var all = await db.Employees.IgnoreTenantFilter(admin).Select(e => e.FullName).OrderBy(x => x).ToListAsync();
        Assert.Equal(["A", "B"], all);
    }

    [Fact]
    public void TenantFilterBypass_throws_for_non_platform_admin()
    {
        var tenant = FixedTenant(Guid.NewGuid(), platformAdmin: false);
        using var db = CreateDb(tenant);
        Assert.Throws<UnauthorizedAccessException>(() =>
            db.Employees.IgnoreTenantFilter(tenant).ToList());
    }
}

public class TenantFilterBypassAndClassificationTests
{
    [Fact]
    public void EntityAuditLog_is_global()
    {
        Assert.True(TenantEntityClassification.IsGlobalEntity(typeof(HRManagement.SharedKernel.Entities.EntityAuditLog)));
        Assert.False(TenantEntityClassification.IsTenantOwned(typeof(HRManagement.SharedKernel.Entities.EntityAuditLog)));
    }

    [Fact]
    public void Employee_is_tenant_owned()
    {
        Assert.True(TenantEntityClassification.IsTenantOwned(typeof(Employee)));
    }
}
