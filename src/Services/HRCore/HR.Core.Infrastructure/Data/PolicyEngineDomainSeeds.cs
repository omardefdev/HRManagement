using System.Text.Json;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Data;

/// <summary>
/// The first version of the national policy on an empty database. PLAN-49 (G-POL-02, plan section 3): only for the domains the
/// catalog governs. The fifteen others get no template, no facts schema and no binding (PolicyDomainCatalog says why for each);
/// a database that has them from an earlier release keeps its history, and the change set POL-049-SCOPE-RETIRE closes each
/// with a final version. Assets.Custody is seeded by PolicyEngineSeeder; the four others here.
/// </summary>
public static class PolicyEngineDomainSeeds
{
    public static async Task SeedAllDomainsAsync(HRCoreDbContext db, CancellationToken ct = default)
    {
        // PLAN-49 (G-POL-11): no rule. The card's approval starts through the cross-service contract (PLAN-46), and the catalog
        // gives the domain Allow and Deny only; an existing database loses R-ID-PROCESS by the change set POL-049-IDCARD-ORPHAN.
        await SeedDomainIfMissingAsync(db, PolicyDomains.IDCardLifecycle, "CardRequest", "بطاقة تعريف — دورة الحياة", ct,
            [],
            [Field("purpose", "string", "الغرض"), Field("action", "string", "الإجراء")]);

        // PLAN-49 (D-POL-5): no R-LEAVE-LONG (RequireApproval has no consumer). The baseline change set adds R-LEAVE-SELF and
        // R-LEAVE-MAX as it always did, and POL-049-LEAVE then retires the placeholder limit and binds R-LEAVE-SELF to Approve.
        await SeedDomainIfMissingAsync(db, PolicyDomains.ContractsLeave, "LeaveRequest", "إجازات — وطنية", ct,
            [],
            [Field("durationDays", "int", "المدة"), Field("leaveType", "string", "النوع"), Field("action", "string", "الإجراء")]);

        // PLAN-25 (G-14): no R-REC-HIRE — the hire is governed by the offer approval (existing databases: the change set
        // POL-2026-10-30-RETIRE-R-REC-HIRE, PLAN-47: a new version without it, never a rewrite).
        await SeedDomainIfMissingAsync(db, PolicyDomains.CoreRecruitment, "RecruitmentApplication", "توظيف", ct,
            [],
            [Field("action", "string", "الإجراء")]);

        await SeedDomainIfMissingAsync(db, PolicyDomains.CoreEstablishment, "EstablishmentChangeRequest", "ملاك — حوكمة", ct,
            [
                Rule("R-EST-SUBMIT", "تقديم تغيير ملاك → workflow", 10,
                    """{"all":[{"field":"action","operator":"Equals","value":"SubmitForApproval"}]}""",
                    """[{"effect":"TriggerWorkflow","params":{"workflowCode":"ESTABLISHMENT-CHANGE-APPROVAL"}}]"""),
                Rule("R-EST-DENY-BRANCH", "رفض تغيير من نطاق فرع", 5,
                    """{"all":[{"field":"action","operator":"Equals","value":"SubmitForApproval"},{"field":"hasOrgScope","operator":"Equals","value":"true"}]}""",
                    """[{"effect":"Deny","params":{"reason":"تغييرات الملاك للموارد البشرية المركزية فقط"}}]""")
            ],
            [
                Field("action", "string", "الإجراء"),
                Field("changeType", "string", "نوع التغيير"),
                Field("changeStatus", "string", "حالة الطلب"),
                Field("hasOrgScope", "bool", "نطاق فرع")
            ]);

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedDomainIfMissingAsync(
        HRCoreDbContext db,
        string domain,
        string subjectType,
        string nameAr,
        CancellationToken ct,
        IReadOnlyList<(string Key, string Name, int Priority, string When, string Then)> rules,
        IReadOnlyList<PolicyFieldSchema> fields)
    {
        if (await db.PolicyTemplates.AnyAsync(t => t.Domain == domain, ct))
            return;

        var template = new PolicyTemplate
        {
            TenantId = TenantDefaults.DefaultTenantId,
            Domain = domain,
            Name = nameAr,
            SubjectType = subjectType,
            Scope = PolicyTemplateScope.National,
            Description = $"seed PROD001ab — {domain}"
        };
        db.PolicyTemplates.Add(template);

        if (!await db.PolicyFactsSchemas.AnyAsync(s => s.Domain == domain, ct))
        {
            db.PolicyFactsSchemas.Add(new PolicyFactsSchemaEntity
            {
                TenantId = TenantDefaults.DefaultTenantId,
                Domain = domain,
                SubjectType = subjectType,
                FieldsJson = JsonSerializer.Serialize(fields)
            });
        }

        // v1 on an empty database only. PLAN-47: rules go only to a draft, so v1 is added as a draft and then published.
        await PolicySeedVersionWriter.AddAsync(db, new PolicyTemplateVersion
        {
            PolicyTemplate = template,
            TenantId = template.TenantId,
            VersionNumber = 1,
            EffectiveFrom = DateTime.UtcNow.AddYears(-1)
        },
        rules.Select(r => new PolicyRule
        {
            TenantId = template.TenantId,
            RuleKey = r.Key,
            Name = r.Name,
            Priority = r.Priority,
            WhenJson = r.When,
            ThenJson = r.Then
        }).ToList(),
        PolicyVersionStatus.Published,
        new PolicyPublication
        {
            PublishedAt = DateTime.UtcNow.AddYears(-1),
            EvidenceRef = $"seed-{domain}",
            Kind = PolicyPublicationKind.SystemSeed
        }, ct);
    }

    private static (string Key, string Name, int Priority, string When, string Then) Rule(
        string key, string name, int priority, string when, string then) => (key, name, priority, when, then);

    private static PolicyFieldSchema Field(string name, string type, string displayAr) =>
        new() { Name = name, Type = type, DisplayNameAr = displayAr };
}
