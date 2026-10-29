using System.Text.Json;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Data;

public static class PolicyEngineSeeder
{
    public static async Task SeedAsync(HRCoreDbContext db, CancellationToken ct = default)
    {
        if (!await db.PolicyTemplates.AnyAsync(ct))
        {
            await SeedAssetsCustodyAsync(db, ct);
            await SeedFactsSchemasAsync(db, ct);
            await PolicyEngineDomainSeeds.SeedAllDomainsAsync(db, ct);
            await db.SaveChangesAsync(ct);
        }
        else
        {
            await SeedSelectCodePolicyRuleIfMissingAsync(db, ct);
        }

        await SeedWorkflowBindingsAsync(db, ct);
        await PolicyEngineDomainSeeds.SeedIncrementalRulesAsync(db, ct);
    }

    private static async Task SeedSelectCodePolicyRuleIfMissingAsync(HRCoreDbContext db, CancellationToken ct)
    {
        if (await db.PolicyRules.AnyAsync(r => r.RuleKey == "R-PRIV-CODE", ct))
            return;

        var version = await db.PolicyTemplateVersions.AsNoTracking()
            .Include(v => v.PolicyTemplate)
            .Where(v => v.PolicyTemplate!.Domain == PolicyDomains.AssetsCustody)
            .Where(v => v.Status == PolicyVersionStatus.Published)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);
        if (version is null)
            return;

        db.PolicyRules.Add(new PolicyRule
        {
            PolicyTemplateVersionId = version.Id,
            RuleKey = "R-PRIV-CODE",
            Name = "مميز → قالب ترميز VIP",
            Priority = 12,
            WhenJson = """{"all":[{"field":"isPrivileged","operator":"Equals","value":"true"},{"field":"action","operator":"Equals","value":"Create"}]}""",
            ThenJson = """[{"effect":"SelectCodePolicy","params":{"policyKey":"ASSET-PRIVILEGED"}}]"""
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedAssetsCustodyAsync(HRCoreDbContext db, CancellationToken ct)
    {
        var template = new PolicyTemplate
        {
            TenantId = TenantDefaults.DefaultTenantId,
            Domain = PolicyDomains.AssetsCustody,
            Name = "سياسة عهدة الأصول — وطنية",
            SubjectType = "Asset",
            Scope = PolicyTemplateScope.National,
            Description = "إلزام دليل للمميز وworkflow للموافقة"
        };
        db.PolicyTemplates.Add(template);

        var version = new PolicyTemplateVersion
        {
            PolicyTemplate = template,
            VersionNumber = 1,
            Status = PolicyVersionStatus.Published,
            EffectiveFrom = DateTime.UtcNow.AddYears(-1)
        };
        db.PolicyTemplateVersions.Add(version);

        db.PolicyRules.Add(new PolicyRule
        {
            PolicyTemplateVersion = version,
            RuleKey = "R-CLASSIFY-HIGH-VALUE",
            Name = "تصنيف — قيمة عالية → مميز",
            Priority = 5,
            WhenJson = """{"all":[{"field":"action","operator":"Equals","value":"Classify"},{"field":"value","operator":"GreaterThan","value":"10000"}]}""",
            ThenJson = """[{"effect":"SetField","params":{"field":"isPrivileged","value":"true"}}]"""
        });

        db.PolicyRules.Add(new PolicyRule
        {
            PolicyTemplateVersion = version,
            RuleKey = "R-CLASSIFY-CAPITAL",
            Name = "تصنيف — أصول ثابتة → مميز",
            Priority = 6,
            WhenJson = """{"all":[{"field":"action","operator":"Equals","value":"Classify"},{"field":"category","operator":"Equals","value":"أصول ثابتة"},{"field":"value","operator":"GreaterThan","value":"5000"}]}""",
            ThenJson = """[{"effect":"SetField","params":{"field":"isPrivileged","value":"true"}}]"""
        });

        db.PolicyRules.Add(new PolicyRule
        {
            PolicyTemplateVersion = version,
            RuleKey = "R-PRIV-EVIDENCE",
            Name = "أصل مميز → دليل",
            Priority = 10,
            WhenJson = """{"all":[{"field":"isPrivileged","operator":"Equals","value":"true"}]}""",
            ThenJson = """[{"effect":"RequireEvidence"}]"""
        });

        db.PolicyRules.Add(new PolicyRule
        {
            PolicyTemplateVersion = version,
            RuleKey = "R-HIGH-APPROVAL",
            Name = "قيمة عالية → موافقة",
            Priority = 20,
            WhenJson = """{"all":[{"field":"value","operator":"GreaterThan","value":"10000"}]}""",
            ThenJson = """[{"effect":"TriggerWorkflow","params":{"workflowCode":"ASSET-ASSIGN-APPROVAL"}}]"""
        });

        db.PolicyRules.Add(new PolicyRule
        {
            PolicyTemplateVersion = version,
            RuleKey = "R-PRIV-WF",
            Name = "مميز → workflow",
            Priority = 15,
            WhenJson = """{"all":[{"field":"isPrivileged","operator":"Equals","value":"true"}]}""",
            ThenJson = """[{"effect":"TriggerWorkflow","params":{"workflowCode":"ASSET-ASSIGN-APPROVAL"}}]"""
        });

        db.PolicyRules.Add(new PolicyRule
        {
            PolicyTemplateVersion = version,
            RuleKey = "R-PRIV-CODE",
            Name = "مميز → قالب ترميز VIP",
            Priority = 12,
            WhenJson = """{"all":[{"field":"isPrivileged","operator":"Equals","value":"true"},{"field":"action","operator":"Equals","value":"Create"}]}""",
            ThenJson = """[{"effect":"SelectCodePolicy","params":{"policyKey":"ASSET-PRIVILEGED"}}]"""
        });

        db.PolicyPublications.Add(new PolicyPublication
        {
            PolicyTemplateVersion = version,
            PublishedAt = DateTime.UtcNow.AddYears(-1),
            EvidenceRef = "seed-PROD001ab"
        });

        await Task.CompletedTask;
    }

    private static async Task SeedFactsSchemasAsync(HRCoreDbContext db, CancellationToken ct)
    {
        if (await db.PolicyFactsSchemas.AnyAsync(s => s.Domain == PolicyDomains.AssetsCustody, ct))
            return;

        var fields = new List<PolicyFieldSchema>
        {
            new() { Name = "isPrivileged", Type = "bool", DisplayNameAr = "أصل مميز" },
            new() { Name = "value", Type = "decimal", DisplayNameAr = "القيمة" },
            new() { Name = "category", Type = "string", DisplayNameAr = "الفئة" },
            new() { Name = "hasOriginReceipt", Type = "bool", DisplayNameAr = "له سجل استلام" },
            new() { Name = "isLegacyEntry", Type = "bool", DisplayNameAr = "قديم/هبة" },
            new() { Name = "action", Type = "string", DisplayNameAr = "الإجراء" }
        };

        db.PolicyFactsSchemas.Add(new PolicyFactsSchemaEntity
        {
            TenantId = TenantDefaults.DefaultTenantId,
            Domain = PolicyDomains.AssetsCustody,
            SubjectType = "Asset",
            FieldsJson = JsonSerializer.Serialize(fields)
        });
    }

    private static readonly (string Domain, string WorkflowCode, string Description)[] WorkflowBindings =
    [
        (PolicyDomains.AssetsCustody, "ASSET-ASSIGN-APPROVAL", "موافقة تسليم عهدة"),
        (PolicyDomains.AssetsCustody, "ASSET-LOSS-APPROVAL", "موافقة فقدان أصل"),
        (PolicyDomains.IDCardLifecycle, "ID-CARD-ISSUANCE", "إصدار بطاقة تعريف"),
        (PolicyDomains.ContractsHire, "HIRE-WIZARD", "معاملة توظيف"),
        (PolicyDomains.ContractsSeparation, "SEPARATION-WIZARD", "معاملة انفصال"),
        (PolicyDomains.MovementsTransfer, "TRANSFER-APPROVAL", "موافقة نقل"),
        (PolicyDomains.CoreRecruitment, "RECRUITMENT-REQUISITION-APPROVAL", "اعتماد طلب شاغر"),
        (PolicyDomains.CoreRecruitment, "RECRUITMENT-OFFER-APPROVAL", "اعتماد خطاب عرض"),
        (PolicyDomains.CoreRecruitment, "RECRUITMENT-OFFER-OVERRIDE-APPROVAL", "اعتماد عرض استثنائي (تجاوز الجدارة)"),
        (PolicyDomains.ComplianceDisciplinary, "DISCIPLINARY-APPROVAL", "إجراء تأديبي"),
        (PolicyDomains.ITTickets, "IT-TICKET", "تذكرة IT")
    ];

    private static async Task SeedWorkflowBindingsAsync(HRCoreDbContext db, CancellationToken ct)
    {
        var added = false;
        foreach (var binding in WorkflowBindings)
        {
            if (await db.PolicyWorkflowBindings.AnyAsync(
                    b => b.TenantId == TenantDefaults.DefaultTenantId && b.WorkflowCode == binding.WorkflowCode, ct))
                continue;

            var def = await db.WorkflowDefinitions.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Code == binding.WorkflowCode, ct);

            db.PolicyWorkflowBindings.Add(new PolicyWorkflowBinding
            {
                TenantId = TenantDefaults.DefaultTenantId,
                Domain = binding.Domain,
                WorkflowCode = binding.WorkflowCode,
                WorkflowDefinitionId = def?.Id,
                Description = binding.Description
            });
            added = true;
        }

        if (added)
            await db.SaveChangesAsync(ct);
    }
}
