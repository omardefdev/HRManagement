using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HRManagement.UnitTests;

public class OrgScopeServiceTests
{
    [Fact]
    public async Task OrgScopeService_expands_branch_subtree()
    {
        var center = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var sub = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new HRCoreDbContext(options);
        db.OrgUnits.AddRange(
            new OrgUnit { Id = center, Code = "C", NameAr = "مركز", TenantId = TenantDefaults.DefaultTenantId },
            new OrgUnit { Id = branch, Code = "B", NameAr = "فرع", ParentId = center, TenantId = TenantDefaults.DefaultTenantId },
            new OrgUnit { Id = sub, Code = "S", NameAr = "فرعي", ParentId = branch, TenantId = TenantDefaults.DefaultTenantId });
        await db.SaveChangesAsync();
        await new OrgUnitPathService(db).RecomputeAllAsync();

        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, HrManagementRoles.BranchManager),
            new Claim(HrOrgScopeVisibility.ScopeOrgUnitClaimType, branch.ToString())
        ], "test"));

        var service = new OrgScopeService(db, new OrgUnitHierarchyCache(new MemoryCache(new MemoryCacheOptions())));
        var scope = await service.GetAllowedOrgUnitIdsAsync(user);

        Assert.False(scope.IsUnrestricted);
        Assert.False(scope.IsDenied);
        Assert.Equal(2, scope.AllowedOrgUnitIds.Count);
        Assert.Contains(branch, scope.AllowedOrgUnitIds);
        Assert.Contains(sub, scope.AllowedOrgUnitIds);
    }
}
