using System.Security.Claims;

using HRManagement.ServiceDefaults;

using HRManagement.SharedKernel.Entities;

using Microsoft.EntityFrameworkCore;



namespace HRManagement.UnitTests;



public class OrgScopeResolverTests

{

    [Fact]

    public async Task Branch_manager_sees_descendants_only()

    {

        var center = Guid.NewGuid();

        var branch = Guid.NewGuid();

        var sub = Guid.NewGuid();

        var sibling = Guid.NewGuid();



        var options = new DbContextOptionsBuilder<ResolverTestDbContext>()

            .UseInMemoryDatabase(Guid.NewGuid().ToString())

            .Options;



        await using var db = new ResolverTestDbContext(options);

        var centerPath = $"/{center:D}/";
        var branchPath = $"/{center:D}/{branch:D}/";
        var subPath = $"/{center:D}/{branch:D}/{sub:D}/";
        var siblingPath = $"/{center:D}/{sibling:D}/";

        db.OrgUnitSnapshots.AddRange(

            new OrgUnitSnapshot { Id = center, ParentId = null, Code = "C", NameAr = "مركز", MaterializedPath = centerPath },

            new OrgUnitSnapshot { Id = branch, ParentId = center, Code = "B", NameAr = "فرع", MaterializedPath = branchPath },

            new OrgUnitSnapshot { Id = sub, ParentId = branch, Code = "S", NameAr = "فرعي", MaterializedPath = subPath },

            new OrgUnitSnapshot { Id = sibling, ParentId = center, Code = "X", NameAr = "شقيق", MaterializedPath = siblingPath });

        await db.SaveChangesAsync();



        var user = new ClaimsPrincipal(new ClaimsIdentity([

            new Claim(ClaimTypes.Role, HrManagementRoles.BranchManager),

            new Claim(HrOrgScopeVisibility.ScopeOrgUnitClaimType, branch.ToString())

        ], "test"));



        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(user, db.OrgUnitSnapshots);



        Assert.False(scope.IsDenied);

        Assert.Contains(branch, scope.AllowedOrgUnitIds);

        Assert.Contains(sub, scope.AllowedOrgUnitIds);

        Assert.DoesNotContain(center, scope.AllowedOrgUnitIds);

        Assert.DoesNotContain(sibling, scope.AllowedOrgUnitIds);

    }



    [Fact]

    public async Task Branch_manager_without_claim_is_denied()

    {

        var options = new DbContextOptionsBuilder<ResolverTestDbContext>()

            .UseInMemoryDatabase(Guid.NewGuid().ToString())

            .Options;

        await using var db = new ResolverTestDbContext(options);



        var user = new ClaimsPrincipal(new ClaimsIdentity([

            new Claim(ClaimTypes.Role, HrManagementRoles.BranchManager)

        ], "test"));



        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(user, db.OrgUnitSnapshots);

        Assert.True(scope.IsDenied);

    }



    private sealed class ResolverTestDbContext(DbContextOptions<ResolverTestDbContext> options) : DbContext(options)

    {

        public DbSet<OrgUnitSnapshot> OrgUnitSnapshots => Set<OrgUnitSnapshot>();

    }

}


