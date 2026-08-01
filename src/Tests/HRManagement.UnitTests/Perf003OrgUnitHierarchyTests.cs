using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HRManagement.UnitTests;

public class Perf003OrgUnitHierarchyTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    [Fact]
    public async Task ValidateNoCycle_rejects_self_and_descendant_parent()
    {
        await using var db = CreateDb();
        var (root, child, grand) = await SeedChainAsync(db, TenantA);
        var svc = new OrgUnitPathService(db);

        var self = await svc.ValidateNoCycleAsync(child.Id, child.Id);
        Assert.False(self.Ok);

        var underDescendant = await svc.ValidateNoCycleAsync(root.Id, grand.Id);
        Assert.False(underDescendant.Ok);

        var ok = await svc.ValidateNoCycleAsync(grand.Id, root.Id);
        Assert.True(ok.Ok);
    }

    [Fact]
    public async Task MoveSubtree_rewrites_paths_without_recompute_all()
    {
        await using var db = CreateDb();
        var path = new OrgUnitPathService(db);

        var root = await AddUnitAsync(db, TenantA, "R", null);
        await path.AssignPathOnCreateAsync(root);
        var branchA = await AddUnitAsync(db, TenantA, "A", root.Id);
        await path.AssignPathOnCreateAsync(branchA);
        var branchB = await AddUnitAsync(db, TenantA, "B", root.Id);
        await path.AssignPathOnCreateAsync(branchB);
        var leaf = await AddUnitAsync(db, TenantA, "L", branchA.Id);
        await path.AssignPathOnCreateAsync(leaf);

        var (ok, err, affected) = await path.MoveSubtreeAsync(branchA.Id, branchB.Id);
        Assert.True(ok, err);
        Assert.True(affected.Count >= 2);

        leaf = await db.OrgUnits.AsNoTracking().SingleAsync(o => o.Code == "L");
        branchA = await db.OrgUnits.AsNoTracking().SingleAsync(o => o.Code == "A");
        branchB = await db.OrgUnits.AsNoTracking().SingleAsync(o => o.Code == "B");

        Assert.Equal(branchB.Id, branchA.ParentId);
        Assert.StartsWith(branchB.MaterializedPath.TrimEnd('/'), branchA.MaterializedPath.TrimEnd('/'));
        Assert.StartsWith(branchA.MaterializedPath, leaf.MaterializedPath);
    }

    [Fact]
    public async Task OrgScope_path_matches_bfs_reference()
    {
        await using var db = CreateDb();
        var path = new OrgUnitPathService(db);
        var root = await AddUnitAsync(db, TenantA, "ROOT", null);
        await path.AssignPathOnCreateAsync(root);
        var c1 = await AddUnitAsync(db, TenantA, "C1", root.Id);
        await path.AssignPathOnCreateAsync(c1);
        var c2 = await AddUnitAsync(db, TenantA, "C2", c1.Id);
        await path.AssignPathOnCreateAsync(c2);
        var other = await AddUnitAsync(db, TenantA, "OTHER", null);
        await path.AssignPathOnCreateAsync(other);

        var cache = new OrgUnitHierarchyCache(new MemoryCache(new MemoryCacheOptions()));
        var scopeSvc = new OrgScopeService(db, cache, new FixedTenantContext(TenantA));
        var scope = await scopeSvc.GetAllowedOrgUnitIdsAsync(UserWithScope(root.Id));

        Assert.True(scope.MustFilter);
        Assert.Contains(root.Id, scope.AllowedOrgUnitIds);
        Assert.Contains(c1.Id, scope.AllowedOrgUnitIds);
        Assert.Contains(c2.Id, scope.AllowedOrgUnitIds);
        Assert.DoesNotContain(other.Id, scope.AllowedOrgUnitIds);
    }

    [Fact]
    public async Task Cache_does_not_leak_across_tenants()
    {
        await using var db = CreateDb();
        var path = new OrgUnitPathService(db);
        var rootA = await AddUnitAsync(db, TenantA, "RA", null);
        await path.AssignPathOnCreateAsync(rootA);
        var rootB = await AddUnitAsync(db, TenantB, "RB", null);
        await path.AssignPathOnCreateAsync(rootB);

        var cache = new OrgUnitHierarchyCache(new MemoryCache(new MemoryCacheOptions()));

        var scopeA = await new OrgScopeService(db, cache, new FixedTenantContext(TenantA))
            .GetAllowedOrgUnitIdsAsync(UserWithScope(rootA.Id));
        var scopeB = await new OrgScopeService(db, cache, new FixedTenantContext(TenantB))
            .GetAllowedOrgUnitIdsAsync(UserWithScope(rootB.Id));

        Assert.Contains(rootA.Id, scopeA.AllowedOrgUnitIds);
        Assert.DoesNotContain(rootB.Id, scopeA.AllowedOrgUnitIds);
        Assert.Contains(rootB.Id, scopeB.AllowedOrgUnitIds);
        Assert.DoesNotContain(rootA.Id, scopeB.AllowedOrgUnitIds);

        cache.InvalidateTenant(TenantA);
        scopeA = await new OrgScopeService(db, cache, new FixedTenantContext(TenantA))
            .GetAllowedOrgUnitIdsAsync(UserWithScope(rootA.Id));
        Assert.Contains(rootA.Id, scopeA.AllowedOrgUnitIds);
    }

    [Fact]
    public void GetDepth_counts_segments()
    {
        Assert.Equal(0, OrgUnitPathService.GetDepth(""));
        Assert.Equal(1, OrgUnitPathService.GetDepth("/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa/"));
        Assert.Equal(2, OrgUnitPathService.GetDepth("/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa/bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb/"));
    }

    private static ClaimsPrincipal UserWithScope(Guid orgUnitId)
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.Role, HrManagementRoles.BranchManager));
        identity.AddClaim(new Claim(HrOrgScopeVisibility.ScopeOrgUnitClaimType, orgUnitId.ToString("D")));
        return new ClaimsPrincipal(identity);
    }

    private static async Task<(OrgUnit root, OrgUnit child, OrgUnit grand)> SeedChainAsync(HRCoreDbContext db, Guid tenantId)
    {
        var path = new OrgUnitPathService(db);
        var root = await AddUnitAsync(db, tenantId, "R", null);
        await path.AssignPathOnCreateAsync(root);
        var child = await AddUnitAsync(db, tenantId, "C", root.Id);
        await path.AssignPathOnCreateAsync(child);
        var grand = await AddUnitAsync(db, tenantId, "G", child.Id);
        await path.AssignPathOnCreateAsync(grand);
        return (root, child, grand);
    }

    private static async Task<OrgUnit> AddUnitAsync(
        HRCoreDbContext db,
        Guid tenantId,
        string code,
        Guid? parentId)
    {
        var unit = new OrgUnit
        {
            TenantId = tenantId,
            Code = code,
            NameAr = code,
            ParentId = parentId,
            MaterializedPath = ""
        };
        db.OrgUnits.Add(unit);
        await db.SaveChangesAsync();
        return unit;
    }

    private static HRCoreDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new HRCoreDbContext(options, DesignTimeTenantContext.Instance);
    }

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }
}
