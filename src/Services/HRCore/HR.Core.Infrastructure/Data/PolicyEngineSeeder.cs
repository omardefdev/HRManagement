using System.Text.Json;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Data;

public static class PolicyEngineSeeder
{
    public static Task SeedAsync(HRCoreDbContext db, CancellationToken ct = default) => SeedAsync(db, null, ct);

    /// <summary>
    /// v1 of every governed domain on an empty database (PLAN-49: the five of PolicyDomainCatalog, and no other), the workflow
    /// bindings, then the seed change sets (PLAN-47): every later change of the seeded rules is a new version with a publication
    /// record, never an edit of a published one (the writers that patched published versions in place are gone). The start-up
    /// inventory of refused rules is not taken here: HR.Core takes it once, after every seed of the profile (the step
    /// policy-rule-inventory of its Program.cs, <see cref="Services.Policy.PolicyPublishedRuleInventory"/>).
    /// </summary>
    public static async Task SeedAsync(HRCoreDbContext db, IServiceProvider? services, CancellationToken ct = default)
    {
        if (!await db.PolicyTemplates.AnyAsync(ct))
        {
            await SeedAssetsCustodyAsync(db, ct);
            await SeedFactsSchemasAsync(db, ct);
            await PolicyEngineDomainSeeds.SeedAllDomainsAsync(db, ct);
            await db.SaveChangesAsync(ct);
        }

        await SeedWorkflowBindingsAsync(db, ct);
        await PolicySeedChangeSetApplier.ApplyAsync(db, services, ct);
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

        // PLAN-47: rules go only to a draft, so v1 is added as a draft with its rules and then published.
        var rules = new List<PolicyRule>();
        rules.Add(new PolicyRule
        {
            RuleKey = "R-CLASSIFY-HIGH-VALUE",
            Name = "تصنيف — قيمة عالية → مميز",
            Priority = 5,
            WhenJson = """{"all":[{"field":"action","operator":"Equals","value":"Classify"},{"field":"value","operator":"GreaterThan","value":"10000"}]}""",
            ThenJson = """[{"effect":"SetField","params":{"field":"isPrivileged","value":"true"}}]"""
        });

        rules.Add(new PolicyRule
        {
            RuleKey = "R-CLASSIFY-CAPITAL",
            Name = "تصنيف — أصول ثابتة → مميز",
            Priority = 6,
            WhenJson = """{"all":[{"field":"action","operator":"Equals","value":"Classify"},{"field":"category","operator":"Equals","value":"أصول ثابتة"},{"field":"value","operator":"GreaterThan","value":"5000"}]}""",
            ThenJson = """[{"effect":"SetField","params":{"field":"isPrivileged","value":"true"}}]"""
        });

        // PLAN-49 (G-POL-02): R-PRIV-EVIDENCE, R-PRIV-WF and R-HIGH-APPROVAL are bound to the handover (action Assign), the only
        // call site that reads their effects. The same rules, from the same place, as the change set POL-049-ASSETS-ACTION gives
        // an existing database, so that set finds nothing to replace here.
        rules.AddRange(PolicySeedChangeSets.CustodyAssignRules.Select(rule => new PolicyRule
        {
            RuleKey = rule.Key,
            Name = rule.Name,
            Priority = rule.Priority,
            WhenJson = rule.When,
            ThenJson = rule.Then
        }));

        rules.Add(new PolicyRule
        {
            RuleKey = "R-PRIV-CODE",
            Name = "مميز → قالب ترميز VIP",
            Priority = 12,
            WhenJson = """{"all":[{"field":"isPrivileged","operator":"Equals","value":"true"},{"field":"action","operator":"Equals","value":"Create"}]}""",
            ThenJson = """[{"effect":"SelectCodePolicy","params":{"policyKey":"ASSET-PRIVILEGED"}}]"""
        });

        foreach (var rule in rules)
            rule.TenantId = template.TenantId;

        await PolicySeedVersionWriter.AddAsync(db, new PolicyTemplateVersion
        {
            PolicyTemplate = template,
            TenantId = template.TenantId,
            VersionNumber = 1,
            EffectiveFrom = DateTime.UtcNow.AddYears(-1)
        }, rules, PolicyVersionStatus.Published, new PolicyPublication
        {
            PublishedAt = DateTime.UtcNow.AddYears(-1),
            EvidenceRef = "seed-PROD001ab",
            Kind = PolicyPublicationKind.SystemSeed
        }, ct);
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

    /// <summary>
    /// PLAN-49 (plan §3): the bindings a database gets are the codes a rule of a governed domain may name (PolicyDomainCatalog).
    /// The seven others that earlier releases wrote stay where they are and are never removed: nothing reads them at run time
    /// (ASSET-LOSS-APPROVAL, ID-CARD-ISSUANCE and DISCIPLINARY-APPROVAL start through the cross-service contract, which reads no
    /// binding; HIRE-WIZARD, SEPARATION-WIZARD, TRANSFER-APPROVAL and IT-TICKET are retired codes), so a new database has none.
    /// </summary>
    private static readonly (string Domain, string WorkflowCode, string Description)[] WorkflowBindings =
    [
        (PolicyDomains.AssetsCustody, "ASSET-ASSIGN-APPROVAL", "موافقة تسليم عهدة"),
        (PolicyDomains.CoreRecruitment, "RECRUITMENT-REQUISITION-APPROVAL", "اعتماد طلب شاغر"),
        (PolicyDomains.CoreRecruitment, "RECRUITMENT-OFFER-APPROVAL", "اعتماد خطاب عرض"),
        (PolicyDomains.CoreRecruitment, "RECRUITMENT-OFFER-OVERRIDE-APPROVAL", "اعتماد عرض استثنائي (تجاوز الجدارة)")
    ];

    /// <summary>
    /// PLAN-47: codes the seeded recruitment rules fire that had no binding. The validator accepts a TriggerWorkflow only for a
    /// code bound to the domain; these bindings pin no definition version (WorkflowStarter then takes the published one).
    /// PLAN-49: and the code R-EST-SUBMIT fires, which never had one (the validator answered THEN_WORKFLOW_UNBOUND for the rule).
    /// Unpinned on purpose: a new version of the definition published by an administrator must be the one that starts.
    /// </summary>
    private static readonly (string Domain, string WorkflowCode, string Description)[] UnpinnedWorkflowBindings =
    [
        (PolicyDomains.CoreRecruitment, "RECRUITMENT-SELECTION-COMMITTEE", "لجنة اختيار"),
        (PolicyDomains.CoreRecruitment, "RECRUITMENT-APPEAL-REVIEW", "مراجعة استئناف"),
        (PolicyDomains.CoreEstablishment, "ESTABLISHMENT-CHANGE-APPROVAL", "اعتماد تغيير ملاك")
    ];

    /// <summary>Every (domain, code) this seeder binds: the test of the catalog compares them with its triggers.</summary>
    public static IEnumerable<(string Domain, string WorkflowCode)> SeededBindings =>
        WorkflowBindings.Concat(UnpinnedWorkflowBindings).Select(b => (b.Domain, b.WorkflowCode));

    private static async Task SeedWorkflowBindingsAsync(HRCoreDbContext db, CancellationToken ct)
    {
        var added = false;
        foreach (var binding in UnpinnedWorkflowBindings)
        {
            if (await db.PolicyWorkflowBindings.AnyAsync(b => b.Domain == binding.Domain && b.WorkflowCode == binding.WorkflowCode, ct))
                continue;

            db.PolicyWorkflowBindings.Add(new PolicyWorkflowBinding
            {
                TenantId = TenantDefaults.DefaultTenantId,
                Domain = binding.Domain,
                WorkflowCode = binding.WorkflowCode,
                WorkflowDefinitionId = null,
                Description = binding.Description
            });
            added = true;
        }

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
