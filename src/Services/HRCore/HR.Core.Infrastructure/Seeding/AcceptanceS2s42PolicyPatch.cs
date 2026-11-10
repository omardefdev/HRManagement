using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Core.Infrastructure.Seeding;

/// <summary>
/// PLAN-42, acceptance only: a new published version of agency A's custody template, with v2's rules and one more that denies a
/// category no real asset has. docker-compose.acceptance-s2s42.yml turns it on with <c>Seed:S2s42PolicyPatch=true</c>, so the lab stack
/// keeps the versions AC-09 and AC-10 count on. Idempotent: a recreated container finds the version and stops.
/// </summary>
public static class AcceptanceS2s42PolicyPatch
{
    public const string SwitchKey = "Seed:S2s42PolicyPatch";
    public const string DeniedCategory = "اختبار-رفض";
    public const string DenyReason = "مرفوض بقاعدة القبول ACC-S2S42-DENY";

    // PLAN-47: the next number of the template (AC-POL-47 publishes versions of this template on the same prod-like stack
    // before the s2s42 overlay recreates hr-core-web), not a fixed 4.

    public static async Task ApplyAsync(HRCoreDbContext db, IConfiguration configuration, CancellationToken ct = default)
    {
        if (!configuration.GetValue(SwitchKey, false))
            return;

        var tenantId = AcceptanceSeedIds.AgencyAId;
        var previousTenant = TenantIdFlow.Get();
        TenantIdFlow.Set(tenantId);
        try
        {
            await PublishAsync(db, tenantId, ct);
        }
        finally
        {
            TenantIdFlow.Set(previousTenant);
        }
    }

    private static async Task PublishAsync(HRCoreDbContext db, Guid tenantId, CancellationToken ct)
    {
        var versionId = AcceptanceSeedCatalog.DeterministicGuid("AGENCY-A", "policy-assets-v4-s2s42");
        var versions = db.PolicyTemplateVersions.OwnOrNational(tenantId);
        if (await versions.AnyAsync(v => v.Id == versionId, ct))
            return;

        var previous = await db.PolicyRules
            .OwnOrNational(tenantId)
            .AsNoTracking()
            .Where(r => r.PolicyTemplateVersionId == AcceptanceSeedIds.PolicyVersion2Id)
            .ToListAsync(ct);

        // PLAN-47: one draft per template, and rules only on a draft. The seeded v3 draft is AC-10's (the lab stack); this stack
        // has no use for it, so it is discarded (a draft may become Archived) before v4 is added as a draft and published.
        var openDrafts = await versions
            .Where(v => v.PolicyTemplateId == AcceptanceSeedIds.PolicyTemplateId && v.Status == PolicyVersionStatus.Draft)
            .ToListAsync(ct);
        foreach (var draft in openDrafts)
            draft.Status = PolicyVersionStatus.Archived;
        if (openDrafts.Count > 0)
            await db.SaveChangesAsync(ct);

        var rules = previous.Select(rule => new PolicyRule
        {
            Id = AcceptanceSeedCatalog.DeterministicGuid("AGENCY-A", "s2s42-" + rule.RuleKey),
            TenantId = tenantId,
            RuleKey = rule.RuleKey,
            Name = rule.Name,
            Priority = rule.Priority,
            WhenJson = rule.WhenJson,
            ThenJson = rule.ThenJson
        }).ToList();
        rules.Add(new PolicyRule
        {
            Id = AcceptanceSeedCatalog.DeterministicGuid("AGENCY-A", "policy-rule-s2s42-deny"),
            TenantId = tenantId,
            RuleKey = "ACC-S2S42-DENY",
            Name = "رفض فئة اختبار القبول",
            Priority = 1,
            WhenJson = """{"all":[{"field":"category","operator":"Equals","value":""" + "\"" + DeniedCategory + "\"" + "}]}",
            ThenJson = """[{"effect":"Deny","params":{"reason":""" + "\"" + DenyReason + "\"" + "}}]"
        });

        await PolicySeedVersionWriter.AddAsync(db, new PolicyTemplateVersion
        {
            Id = versionId,
            TenantId = tenantId,
            PolicyTemplateId = AcceptanceSeedIds.PolicyTemplateId,
            VersionNumber = await versions.Where(v => v.PolicyTemplateId == AcceptanceSeedIds.PolicyTemplateId).MaxAsync(v => v.VersionNumber, ct) + 1,
            EffectiveFrom = AcceptanceSeedCatalog.ReferenceDate
        }, rules, PolicyVersionStatus.Published, new PolicyPublication
        {
            Id = AcceptanceSeedCatalog.DeterministicGuid("AGENCY-A", "policy-publication-v4-s2s42"),
            TenantId = tenantId,
            PublishedAt = AcceptanceSeedCatalog.ReferenceDate,
            EvidenceRef = "AC-S2S-42"
        }, ct);
    }
}
