using System.Security.Claims;

using HR.Core.Domain.Entities;

using HR.Core.Infrastructure;

using HR.Core.Infrastructure.Data;

using HRManagement.ServiceDefaults;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;



namespace HRManagement.UnitTests;



public class HrOrgScopeVisibilityTests

{

    [Fact]

    public void BranchManager_Is_Scoped_Admin_Is_Not()

    {

        var branch = new ClaimsPrincipal(new ClaimsIdentity([

            new Claim(ClaimTypes.Role, HrManagementRoles.BranchManager),

            new Claim(HrOrgScopeVisibility.ScopeOrgUnitClaimType, Guid.NewGuid().ToString())

        ], "test"));



        var admin = new ClaimsPrincipal(new ClaimsIdentity([

            new Claim(ClaimTypes.Role, HrManagementRoles.Admin)

        ], "test"));



        Assert.True(HrOrgScopeVisibility.IsBranchScoped(branch));

        Assert.False(HrOrgScopeVisibility.IsBranchScoped(admin));

        Assert.Equal(OrgScopeClaimKind.Valid, HrOrgScopeVisibility.GetScopeClaimResult(branch).Kind);

        Assert.Equal(OrgScopeClaimKind.NotScoped, HrOrgScopeVisibility.GetScopeClaimResult(admin).Kind);

        Assert.NotNull(HrOrgScopeVisibility.GetScopeRootOrgUnitId(branch));

        Assert.Null(HrOrgScopeVisibility.GetScopeRootOrgUnitId(admin));

    }



    [Fact]

    public void BranchManager_WithoutClaim_Is_InvalidClaim()

    {

        var branch = new ClaimsPrincipal(new ClaimsIdentity([

            new Claim(ClaimTypes.Role, HrManagementRoles.BranchManager)

        ], "test"));



        Assert.Equal(OrgScopeClaimKind.InvalidClaim, HrOrgScopeVisibility.GetScopeClaimResult(branch).Kind);

    }



    [Fact]

    public void BranchManager_WithInvalidClaim_Is_InvalidClaim()

    {

        var branch = new ClaimsPrincipal(new ClaimsIdentity([

            new Claim(ClaimTypes.Role, HrManagementRoles.BranchManager),

            new Claim(HrOrgScopeVisibility.ScopeOrgUnitClaimType, "not-a-guid")

        ], "test"));



        Assert.Equal(OrgScopeClaimKind.InvalidClaim, HrOrgScopeVisibility.GetScopeClaimResult(branch).Kind);

    }



    [Fact]

    public void CanAccessOrgUnit_Respects_Allowed_Set()

    {

        var id = Guid.NewGuid();

        var restricted = OrgAccessScope.Restricted(new HashSet<Guid> { id });

        Assert.True(restricted.CanAccess(id));

        Assert.False(restricted.CanAccess(Guid.NewGuid()));

        Assert.True(OrgAccessScope.Unrestricted().CanAccess(Guid.NewGuid()));

        Assert.False(OrgAccessScope.Denied().CanAccess(id));

        Assert.False(OrgAccessScope.Denied().CanAccess(null));

    }

}



public class OrgScopeFailClosedTests

{

    private static ClaimsPrincipal Branch(Guid? root = null, bool includeClaim = true)

    {

        var claims = new List<Claim> { new(ClaimTypes.Role, HrManagementRoles.BranchManager) };

        if (includeClaim && root.HasValue)

            claims.Add(new Claim(HrOrgScopeVisibility.ScopeOrgUnitClaimType, root.Value.ToString()));

        else if (includeClaim && !root.HasValue)

            claims.Add(new Claim(HrOrgScopeVisibility.ScopeOrgUnitClaimType, "bad"));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

    }



    [Fact]

    public async Task OrgScopeService_BranchManager_WithoutClaim_IsDenied()

    {

        var options = new DbContextOptionsBuilder<HRCoreDbContext>()

            .UseInMemoryDatabase(Guid.NewGuid().ToString())

            .Options;

        await using var db = new HRCoreDbContext(options);

        var service = new OrgScopeService(db, new OrgUnitHierarchyCache(new MemoryCache(new MemoryCacheOptions())));

        var scope = await service.GetAllowedOrgUnitIdsAsync(Branch(includeClaim: false));

        Assert.True(scope.IsDenied);

        Assert.False(scope.CanAccess(Guid.NewGuid()));

    }



    [Fact]

    public async Task OrgScopeService_BranchManager_CannotAccessSibling()

    {

        var center = Guid.NewGuid();

        var branch = Guid.NewGuid();

        var sibling = Guid.NewGuid();



        var options = new DbContextOptionsBuilder<HRCoreDbContext>()

            .UseInMemoryDatabase(Guid.NewGuid().ToString())

            .Options;

        await using var db = new HRCoreDbContext(options);

        db.OrgUnits.AddRange(

            new OrgUnit { Id = center, Code = "C", NameAr = "مركز" },

            new OrgUnit { Id = branch, Code = "B", NameAr = "فرع", ParentId = center },

            new OrgUnit { Id = sibling, Code = "X", NameAr = "شقيق", ParentId = center });

        await db.SaveChangesAsync();
        await new OrgUnitPathService(db).RecomputeAllAsync();

        var service = new OrgScopeService(db, new OrgUnitHierarchyCache(new MemoryCache(new MemoryCacheOptions())));

        var scope = await service.GetAllowedOrgUnitIdsAsync(Branch(branch));

        Assert.False(scope.IsDenied);

        Assert.True(scope.CanAccess(branch));

        Assert.False(scope.CanAccess(sibling));

        Assert.False(scope.CanAccess(center));

    }



    [Fact]

    public async Task OrgScopeService_Admin_IsUnrestricted()

    {

        var options = new DbContextOptionsBuilder<HRCoreDbContext>()

            .UseInMemoryDatabase(Guid.NewGuid().ToString())

            .Options;

        await using var db = new HRCoreDbContext(options);

        var admin = new ClaimsPrincipal(new ClaimsIdentity([

            new Claim(ClaimTypes.Role, HrManagementRoles.Admin)

        ], "test"));

        var scope = await new OrgScopeService(db, new OrgUnitHierarchyCache(new MemoryCache(new MemoryCacheOptions()))).GetAllowedOrgUnitIdsAsync(admin);

        Assert.True(scope.IsUnrestricted);

        Assert.True(scope.CanAccess(Guid.NewGuid()));

    }

}


