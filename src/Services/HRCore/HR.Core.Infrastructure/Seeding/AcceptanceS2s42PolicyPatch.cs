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

    private const int VersionNumber = 4;

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
        var versions = db.PolicyTemplateVersions.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization);
        if (await versions.AnyAsync(v => v.Id == versionId, ct))
            return;

        var previous = await db.PolicyRules
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
            .AsNoTracking()
            .Where(r => r.PolicyTemplateVersionId == AcceptanceSeedIds.PolicyVersion2Id)
            .ToListAsync(ct);

        db.PolicyTemplateVersions.Add(new PolicyTemplateVersion
        {
            Id = versionId,
            TenantId = tenantId,
            PolicyTemplateId = AcceptanceSeedIds.PolicyTemplateId,
            VersionNumber = VersionNumber,
            Status = PolicyVersionStatus.Published,
            EffectiveFrom = AcceptanceSeedCatalog.ReferenceDate
        });
        foreach (var rule in previous)
        {
            db.PolicyRules.Add(new PolicyRule
            {
                Id = AcceptanceSeedCatalog.DeterministicGuid("AGENCY-A", "s2s42-" + rule.RuleKey),
                TenantId = tenantId,
                PolicyTemplateVersionId = versionId,
                RuleKey = rule.RuleKey,
                Name = rule.Name,
                Priority = rule.Priority,
                WhenJson = rule.WhenJson,
                ThenJson = rule.ThenJson
            });
        }

        db.PolicyRules.Add(new PolicyRule
        {
            Id = AcceptanceSeedCatalog.DeterministicGuid("AGENCY-A", "policy-rule-s2s42-deny"),
            TenantId = tenantId,
            PolicyTemplateVersionId = versionId,
            RuleKey = "ACC-S2S42-DENY",
            Name = "رفض فئة اختبار القبول",
            Priority = 1,
            WhenJson = """{"all":[{"field":"category","operator":"Equals","value":""" + "\"" + DeniedCategory + "\"" + "}]}",
            ThenJson = """[{"effect":"Deny","params":{"reason":""" + "\"" + DenyReason + "\"" + "}}]"
        });
        db.PolicyPublications.Add(new PolicyPublication
        {
            Id = AcceptanceSeedCatalog.DeterministicGuid("AGENCY-A", "policy-publication-v4-s2s42"),
            TenantId = tenantId,
            PolicyTemplateVersionId = versionId,
            PublishedAt = AcceptanceSeedCatalog.ReferenceDate,
            EvidenceRef = "AC-S2S-42"
        });
        await db.SaveChangesAsync(ct);
    }
}
