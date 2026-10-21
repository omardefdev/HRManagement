using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Seeding;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.UnitTests;

public class AcceptanceSeedTests
{
    [Fact]
    public async Task Acceptance_seed_is_idempotent()
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db1 = new HRCoreDbContext(options);
        await AcceptanceDataSeeder.SeedAsync(db1, NullLogger.Instance);

        Assert.Equal(3, await db1.Tenants.IgnoreQueryFilters().CountAsync());
        Assert.Equal(98, await db1.OrgUnits.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1617, await db1.Employees.IgnoreQueryFilters().CountAsync());

        await using var db2 = new HRCoreDbContext(options);
        await AcceptanceDataSeeder.SeedAsync(db2, NullLogger.Instance);
        Assert.Equal(3, await db2.Tenants.IgnoreQueryFilters().CountAsync());
        Assert.Equal(98, await db2.OrgUnits.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1617, await db2.Employees.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public void AcceptanceSeedIds_are_stable()
    {
        Assert.Equal("AGENCY-A", AcceptanceSeedIds.AgencyACode);
        Assert.Equal(AcceptanceSeedIds.Br1OrgUnitId, Guid.Parse("a0000001-0000-4000-8000-000000000102"));
        Assert.Equal(AcceptanceSeedIds.EmpBId, Guid.Parse("a0000002-0000-4000-8000-000000000201"));
    }
}
