using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>
/// TEN-002C: <c>BaseEntity.TenantId</c> is model-bindable, so a request could post another
/// agency's tenant id and have it persisted. These cover the write side of tenant isolation —
/// the read side lives in <see cref="CrossTenantIsolationTests"/>.
/// </summary>
public class TenantWriteIsolationTests
{
    private static ITenantContext Tenant(Guid? tenantId, bool platformAdmin = false)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        mock.Setup(t => t.IsPlatformOperator).Returns(platformAdmin);
        if (tenantId.HasValue)
            mock.Setup(t => t.RequireTenantId()).Returns(tenantId.Value);
        else
            mock.Setup(t => t.RequireTenantId()).Throws<InvalidOperationException>();
        return mock.Object;
    }

    private static HRCoreDbContext CreateDb(ITenantContext tenant, string? name = null)
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(name ?? $"tenant-write-{Guid.NewGuid()}")
            .AddInterceptors(new TenantIdStampInterceptor(tenant))
            .Options;
        return new HRCoreDbContext(options, tenant);
    }

    private static Employee NewEmployee(Guid tenantId, string name = "x") => new()
    {
        TenantId = tenantId,
        FullName = name,
        NationalId = $"N-{Guid.NewGuid():N}"[..20]
    };

    [Fact]
    public async Task Insert_carrying_another_tenant_id_is_rejected()
    {
        var caller = Guid.NewGuid();
        var victim = Guid.NewGuid();

        await using var db = CreateDb(Tenant(caller));
        db.Employees.Add(NewEmployee(victim));

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => db.SaveChangesAsync());
        Assert.Contains(victim.ToString(), ex.Message);
    }

    [Fact]
    public async Task Insert_with_empty_tenant_is_stamped_from_the_request()
    {
        var caller = Guid.NewGuid();

        await using var db = CreateDb(Tenant(caller));
        var employee = NewEmployee(Guid.Empty);
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        Assert.Equal(caller, employee.TenantId);
    }

    [Fact]
    public async Task Insert_with_matching_tenant_is_allowed()
    {
        var caller = Guid.NewGuid();

        await using var db = CreateDb(Tenant(caller));
        db.Employees.Add(NewEmployee(caller));

        await db.SaveChangesAsync();
        Assert.Equal(1, await db.Employees.CountAsync());
    }

    [Fact]
    public async Task Update_cannot_move_a_row_to_another_tenant()
    {
        var caller = Guid.NewGuid();
        var victim = Guid.NewGuid();
        var dbName = $"tenant-move-{Guid.NewGuid()}";

        Guid employeeId;
        await using (var seed = CreateDb(Tenant(caller), dbName))
        {
            var employee = NewEmployee(caller);
            seed.Employees.Add(employee);
            await seed.SaveChangesAsync();
            employeeId = employee.Id;
        }

        await using var db = CreateDb(Tenant(caller), dbName);
        var tracked = await db.Employees.FirstAsync(e => e.Id == employeeId);
        tracked.TenantId = victim;

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => db.SaveChangesAsync());
        Assert.Contains("Refusing to move", ex.Message);
    }

    [Fact]
    public async Task Platform_operator_may_write_for_another_tenant()
    {
        var operatorTenant = Guid.NewGuid();
        var target = Guid.NewGuid();

        await using var db = CreateDb(Tenant(operatorTenant, platformAdmin: true));
        var employee = NewEmployee(target);
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        Assert.Equal(target, employee.TenantId);
    }

    [Fact]
    public async Task Explicit_scope_allows_server_side_provisioning_for_another_tenant()
    {
        var caller = Guid.NewGuid();
        var target = Guid.NewGuid();

        await using var db = CreateDb(Tenant(caller));
        db.Employees.Add(NewEmployee(target));

        using (TenantStampScope.AllowExplicitTenant())
        {
            await db.SaveChangesAsync();
        }

        Assert.Equal(1, await db.Employees.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Explicit_scope_does_not_leak_past_its_using_block()
    {
        var caller = Guid.NewGuid();
        var target = Guid.NewGuid();

        using (TenantStampScope.AllowExplicitTenant())
        {
            // scope opened and closed without writing
        }

        await using var db = CreateDb(Tenant(caller));
        db.Employees.Add(NewEmployee(target));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Background_work_without_a_request_still_requires_an_explicit_tenant()
    {
        await using var db = CreateDb(Tenant(null));
        db.Employees.Add(NewEmployee(Guid.Empty));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("without TenantId", ex.Message);
    }

    [Fact]
    public async Task Background_work_may_supply_its_own_tenant()
    {
        var target = Guid.NewGuid();

        await using var db = CreateDb(Tenant(null));
        var employee = NewEmployee(target);
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        Assert.Equal(target, employee.TenantId);
    }
}
