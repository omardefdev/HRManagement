using HR.Core.Domain.Entities.Policy;

namespace HR.Core.Infrastructure.Data;

/// <summary>
/// PLAN-47 (§3): rules are written only to a draft (the interceptor and the PostgreSQL trigger refuse anything else). A seeder
/// that creates a published or archived version therefore adds it as a draft with its rules, saves, and only then moves it to its
/// final status. The caller's transaction, if any, holds both saves. One version at a time: one draft per template (D-POL-12).
/// </summary>
public static class PolicySeedVersionWriter
{
    public static async Task<PolicyTemplateVersion> AddAsync(
        HRCoreDbContext db,
        PolicyTemplateVersion version,
        IEnumerable<PolicyRule> rules,
        PolicyVersionStatus finalStatus,
        PolicyPublication? publication,
        CancellationToken ct = default)
    {
        version.Status = PolicyVersionStatus.Draft;
        db.PolicyTemplateVersions.Add(version);
        foreach (var rule in rules)
        {
            rule.PolicyTemplateVersion = version;
            rule.PolicyTemplateVersionId = version.Id;
            if (rule.TenantId == Guid.Empty)
                rule.TenantId = version.TenantId;
            db.PolicyRules.Add(rule);
        }

        await db.SaveChangesAsync(ct);

        if (finalStatus != PolicyVersionStatus.Draft)
        {
            version.Status = finalStatus;
            if (publication is not null)
            {
                publication.PolicyTemplateVersion = version;
                publication.PolicyTemplateVersionId = version.Id;
                if (publication.TenantId == Guid.Empty)
                    publication.TenantId = version.TenantId;
                db.PolicyPublications.Add(publication);
            }

            await db.SaveChangesAsync(ct);
        }

        return version;
    }
}
