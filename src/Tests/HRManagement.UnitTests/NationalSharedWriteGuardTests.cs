using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.MasterData;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-48 (§1, rule 1): the guard's own negative tests. A national row (an <see cref="INationalShared"/> entity of the national
/// tenant) is added, changed or deleted only from the national tenant's session or an explicit seed scope; every HR.Core context
/// carries the guard (it is registered inside <see cref="HRCoreDbContext"/>).
/// </summary>
public class NationalSharedWriteGuardTests
{
    private static readonly Guid AgencyB = Guid.Parse("a0000002-0000-4000-8000-000000000001");

    private sealed class FixedTenant(Guid? tenantId, bool platformOperator = false) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public bool IsPlatformOperator => platformOperator;
        public Guid RequireTenantId() => tenantId ?? throw new InvalidOperationException("no tenant");
    }

    private static DbContextOptions<HRCoreDbContext> Options(string name) =>
        new DbContextOptionsBuilder<HRCoreDbContext>().UseInMemoryDatabase(name).Options;

    /// <summary>A design-time context (no tenant context) seeds one national template and one national reference entry.</summary>
    private static async Task<(string Db, Guid Template, Guid Reference)> SeedAsync()
    {
        var name = Guid.NewGuid().ToString();
        await using var seed = new HRCoreDbContext(Options(name));
        var template = new CodePolicyTemplate { TenantId = NationalTenant.Id, Name = "national", EntityKind = CodeEntityKind.AssetTag };
        var reference = new ReferenceCatalogEntry { TenantId = NationalTenant.Id, CatalogKind = CatalogKind.Grade, Code = "G1", NameAr = "وطني" };
        seed.CodePolicyTemplates.Add(template);
        seed.ReferenceCatalogEntries.Add(reference);
        await seed.SaveChangesAsync();
        return (name, template.Id, reference.Id);
    }

    [Fact]
    public async Task An_agency_session_cannot_change_delete_or_add_a_national_row()
    {
        var (name, templateId, referenceId) = await SeedAsync();

        await using (var db = new HRCoreDbContext(Options(name), new FixedTenant(AgencyB)))
        {
            var template = await db.CodePolicyTemplates.OwnOrNational(AgencyB).FirstAsync(t => t.Id == templateId);
            template.Name = "agency edit";
            var changed = await Assert.ThrowsAsync<NationalSharedWriteRefusedException>(() => db.SaveChangesAsync());
            Assert.Equal(EntityState.Modified, changed.State);
            Assert.Equal(nameof(CodePolicyTemplate), changed.EntityName);
            Assert.Equal(AgencyB, changed.SessionTenantId);
        }

        await using (var db = new HRCoreDbContext(Options(name), new FixedTenant(AgencyB)))
        {
            db.ReferenceCatalogEntries.Remove(await db.ReferenceCatalogEntries.OwnOrNational(AgencyB).FirstAsync(e => e.Id == referenceId));
            var deleted = await Assert.ThrowsAsync<NationalSharedWriteRefusedException>(() => db.SaveChangesAsync());
            Assert.Equal(EntityState.Deleted, deleted.State);
        }

        await using (var db = new HRCoreDbContext(Options(name), new FixedTenant(AgencyB)))
        {
            db.PolicyRules.Add(new PolicyRule { TenantId = NationalTenant.Id, RuleKey = "R-CHILD", PolicyTemplateVersionId = Guid.NewGuid() });
            var added = await Assert.ThrowsAsync<NationalSharedWriteRefusedException>(() => db.SaveChangesAsync());
            Assert.Equal(EntityState.Added, added.State);
            Assert.IsAssignableFrom<UnauthorizedAccessException>(added);
        }

        await using (var check = new HRCoreDbContext(Options(name)))
        {
            Assert.Equal("national", (await check.CodePolicyTemplates.SingleAsync()).Name);
            Assert.Equal(1, await check.ReferenceCatalogEntries.CountAsync());
            Assert.Equal(0, await check.PolicyRules.CountAsync());
        }
    }

    [Fact]
    public async Task A_row_cannot_move_into_or_out_of_the_national_tenant_from_an_agency()
    {
        var (name, _, referenceId) = await SeedAsync();
        await using (var db = new HRCoreDbContext(Options(name), new FixedTenant(AgencyB)))
        {
            var own = new ReferenceCatalogEntry { TenantId = AgencyB, CatalogKind = CatalogKind.Grade, Code = "G2", NameAr = "جهة" };
            db.ReferenceCatalogEntries.Add(own);
            await db.SaveChangesAsync();

            own.TenantId = NationalTenant.Id;
            await Assert.ThrowsAsync<NationalSharedWriteRefusedException>(() => db.SaveChangesAsync());
        }

        await using (var db = new HRCoreDbContext(Options(name), new FixedTenant(AgencyB)))
        {
            var national = await db.ReferenceCatalogEntries.OwnOrNational(AgencyB).FirstAsync(e => e.Id == referenceId);
            national.TenantId = AgencyB;
            await Assert.ThrowsAsync<NationalSharedWriteRefusedException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task A_platform_operator_who_entered_an_agency_is_refused_too()
    {
        var (name, templateId, _) = await SeedAsync();
        await using var db = new HRCoreDbContext(Options(name), new FixedTenant(AgencyB, platformOperator: true));
        (await db.CodePolicyTemplates.OwnOrNational(AgencyB).FirstAsync(t => t.Id == templateId)).Name = "operator edit";
        await Assert.ThrowsAsync<NationalSharedWriteRefusedException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_session_with_no_tenant_is_refused()
    {
        var (name, templateId, _) = await SeedAsync();
        await using var db = new HRCoreDbContext(Options(name), new FixedTenant(null));
        (await db.CodePolicyTemplates.NationalOnly().FirstAsync(t => t.Id == templateId)).Name = "nobody";
        await Assert.ThrowsAsync<NationalSharedWriteRefusedException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task The_national_session_an_explicit_seed_scope_and_design_time_tooling_may_write()
    {
        var (name, templateId, _) = await SeedAsync();

        await using (var db = new HRCoreDbContext(Options(name), new FixedTenant(NationalTenant.Id)))
        {
            (await db.CodePolicyTemplates.FirstAsync(t => t.Id == templateId)).Name = "national edit";
            db.CodePolicyTemplates.Add(new CodePolicyTemplate { TenantId = NationalTenant.Id, Name = "added", EntityKind = CodeEntityKind.LegalCase });
            await db.SaveChangesAsync();
        }

        await using (var db = new HRCoreDbContext(Options(name), new FixedTenant(AgencyB)))
        {
            using (TenantStampScope.AllowExplicitTenant())
            {
                (await db.CodePolicyTemplates.OwnOrNational(AgencyB).FirstAsync(t => t.Id == templateId)).Name = "seed edit";
                await db.SaveChangesAsync();
            }
        }

        await using (var db = new HRCoreDbContext(Options(name)))
        {
            (await db.CodePolicyTemplates.FirstAsync(t => t.Id == templateId)).Name = "tooling edit";
            await db.SaveChangesAsync();
        }

        await using var check = new HRCoreDbContext(Options(name));
        Assert.Equal("tooling edit", (await check.CodePolicyTemplates.FirstAsync(t => t.Id == templateId)).Name);
        Assert.Equal(2, await check.CodePolicyTemplates.CountAsync());
    }

    [Fact]
    public async Task An_agency_writes_its_own_rows_beside_the_national_ones()
    {
        var (name, _, _) = await SeedAsync();
        await using var db = new HRCoreDbContext(Options(name), new FixedTenant(AgencyB));
        db.CodePolicyTemplates.Add(new CodePolicyTemplate
        {
            TenantId = AgencyB,
            Name = "overlay",
            EntityKind = CodeEntityKind.AssetTag,
            Scope = CodePolicyScope.TenantOverride
        });
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.CodePolicyTemplates.OwnOrNational(AgencyB).CountAsync());
        Assert.Equal(1, await db.CodePolicyTemplates.CountAsync());
    }
}
