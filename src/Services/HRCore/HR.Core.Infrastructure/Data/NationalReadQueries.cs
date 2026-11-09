using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Domain.Entities.Policy;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;

namespace HR.Core.Infrastructure.Data;

/// <summary>
/// PLAN-48 (D-TEN-1): what a session may read of the two scoped national tables and their versions. Its own rows, of any scope,
/// and the national rows of <c>Scope = National</c> only: an agency never reads the trial agency's overrides that live in the
/// national tenant (PLATFORM), even though row security would let it see them. The national tenant itself reads all of its rows,
/// and a design-time context (no tenant context) reads everything, as its EF filter does.
/// </summary>
public static class NationalReadQueries
{
    public static IQueryable<PolicyTemplate> ReadablePolicyTemplates(this HRCoreDbContext db)
    {
        var own = db.CurrentTenantId;
        return db.PolicyTemplates.OwnOrNational(db)
            .Where(t => db.BypassTenantFilter || t.TenantId == own || t.Scope == PolicyTemplateScope.National);
    }

    public static IQueryable<PolicyTemplateVersion> ReadablePolicyVersions(this HRCoreDbContext db)
    {
        var own = db.CurrentTenantId;
        return db.PolicyTemplateVersions.OwnOrNational(db)
            .Where(v => db.BypassTenantFilter || v.TenantId == own || v.PolicyTemplate.Scope == PolicyTemplateScope.National);
    }

    public static IQueryable<CodePolicyTemplate> ReadableCodePolicyTemplates(this HRCoreDbContext db)
    {
        var own = db.CurrentTenantId;
        return db.CodePolicyTemplates.OwnOrNational(db)
            .Where(t => db.BypassTenantFilter || t.TenantId == own || t.Scope == CodePolicyScope.National);
    }

    /// <summary>True when the row is national and the session is not the national tenant: shown, never written.</summary>
    public static bool IsInheritedNational(this HRCoreDbContext db, Guid rowTenantId) =>
        rowTenantId == NationalTenant.Id && !NationalTenant.IsNational(db.CurrentTenantId);
}
