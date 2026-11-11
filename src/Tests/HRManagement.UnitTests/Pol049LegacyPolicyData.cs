using System.Text.Json;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-49: the national policy data of a database from before this plan, in the shape the demo server had on 2026-11-10 (tag
/// 8abd5aa7fe93): twenty templates, each on version 1 with the rules earlier releases wrote into it in place, their facts
/// schemas, and the thirteen workflow bindings. PLAN-49's seeders write only the five governed domains, so the tests that
/// prove what its change sets do to an existing database build that database here. Nothing in it is validated: it was not when
/// it was written. This file is compiled into the unit and the integration test projects.
/// </summary>
internal static class Pol049LegacyPolicyData
{
    public sealed record LegacyRule(string Key, string Name, int Priority, string When, string Then);

    public sealed record LegacyField(string Name, string Type, string DisplayAr);

    public sealed record LegacyDomain(string Domain, string SubjectType, string Name, IReadOnlyList<LegacyRule> Rules, IReadOnlyList<LegacyField> Fields);

    private static LegacyRule Rule(string key, string name, int priority, string when, string then) => new(key, name, priority, when, then);

    private static LegacyField Field(string name, string type, string displayAr) => new(name, type, displayAr);

    /// <summary>The rules and fields the PLAN-47 baseline change set names for a domain: on an old database they were written in place.</summary>
    private static IEnumerable<LegacyRule> BaselineRules(string domain) =>
        PolicySeedChangeSets.Baseline.For(domain)
            .Where(o => o.Kind == PolicySeedOperationKind.AddRule)
            .Select(o => new LegacyRule(o.RuleKey!, o.Name!, o.Priority, o.WhenJson!, o.ThenJson!));

    private static IEnumerable<LegacyField> BaselineFields(string domain) =>
        PolicySeedChangeSets.Baseline.For(domain)
            .Where(o => o.Kind == PolicySeedOperationKind.EnsureFactsField)
            .Select(o => new LegacyField(o.FieldName!, o.FieldType!, o.FieldDisplayAr!));

    public static readonly IReadOnlyList<LegacyDomain> Domains =
    [
        // The three custody rules with no condition on the action are the old ones (POL-049-ASSETS-ACTION replaces them).
        new(PolicyDomains.AssetsCustody, "Asset", "سياسة عهدة الأصول — وطنية",
            [
                .. BaselineRules(PolicyDomains.AssetsCustody),
                Rule("R-PRIV-EVIDENCE", "أصل مميز → دليل", 10,
                    """{"all":[{"field":"isPrivileged","operator":"Equals","value":"true"}]}""",
                    """[{"effect":"RequireEvidence"}]"""),
                Rule("R-PRIV-WF", "مميز → workflow", 15,
                    """{"all":[{"field":"isPrivileged","operator":"Equals","value":"true"}]}""",
                    """[{"effect":"TriggerWorkflow","params":{"workflowCode":"ASSET-ASSIGN-APPROVAL"}}]"""),
                Rule("R-HIGH-APPROVAL", "قيمة عالية → موافقة", 20,
                    """{"all":[{"field":"value","operator":"GreaterThan","value":"10000"}]}""",
                    """[{"effect":"TriggerWorkflow","params":{"workflowCode":"ASSET-ASSIGN-APPROVAL"}}]""")
            ],
            [
                Field("isPrivileged", "bool", "أصل مميز"), Field("value", "decimal", "القيمة"), Field("category", "string", "الفئة"),
                Field("hasOriginReceipt", "bool", "له سجل استلام"), Field("isLegacyEntry", "bool", "قديم/هبة"), Field("action", "string", "الإجراء")
            ]),
        new(PolicyDomains.IDCardLifecycle, "CardRequest", "بطاقة تعريف — دورة الحياة",
            [
                Rule("R-ID-PROCESS", "معالجة → workflow", 10,
                    """{"all":[{"field":"action","operator":"Equals","value":"Process"}]}""",
                    """[{"effect":"TriggerWorkflow","params":{"workflowCode":"ID-CARD-ISSUANCE"}}]""")
            ],
            [Field("purpose", "string", "الغرض"), Field("action", "string", "الإجراء")]),
        new(PolicyDomains.ContractsLeave, "LeaveRequest", "إجازات — وطنية",
            [
                Rule("R-LEAVE-LONG", "إجازة طويلة → موافقة", 10,
                    """{"all":[{"field":"durationDays","operator":"GreaterThan","value":"14"}]}""",
                    """[{"effect":"RequireApproval"}]"""),
                .. BaselineRules(PolicyDomains.ContractsLeave)
            ],
            [
                Field("durationDays", "int", "المدة"), Field("leaveType", "string", "النوع"), Field("action", "string", "الإجراء"),
                .. BaselineFields(PolicyDomains.ContractsLeave)
            ]),
        new(PolicyDomains.CoreRecruitment, "RecruitmentApplication", "توظيف",
            [.. BaselineRules(PolicyDomains.CoreRecruitment)],
            [Field("action", "string", "الإجراء"), .. BaselineFields(PolicyDomains.CoreRecruitment)]),
        new(PolicyDomains.CoreEstablishment, "EstablishmentChangeRequest", "ملاك — حوكمة",
            [
                Rule("R-EST-SUBMIT", "تقديم تغيير ملاك → workflow", 10,
                    """{"all":[{"field":"action","operator":"Equals","value":"SubmitForApproval"}]}""",
                    """[{"effect":"TriggerWorkflow","params":{"workflowCode":"ESTABLISHMENT-CHANGE-APPROVAL"}}]"""),
                Rule("R-EST-DENY-BRANCH", "رفض تغيير من نطاق فرع", 5,
                    """{"all":[{"field":"action","operator":"Equals","value":"SubmitForApproval"},{"field":"hasOrgScope","operator":"Equals","value":"true"}]}""",
                    """[{"effect":"Deny","params":{"reason":"تغييرات الملاك للموارد البشرية المركزية فقط"}}]""")
            ],
            [
                Field("action", "string", "الإجراء"), Field("changeType", "string", "نوع التغيير"),
                Field("changeStatus", "string", "حالة الطلب"), Field("hasOrgScope", "bool", "نطاق فرع")
            ]),

        // The fifteen domains PLAN-49 puts out of the engine's scope, as earlier releases seeded them.
        new(PolicyDomains.ContractsAdvance, "AdvanceRequest", "سلف — وطنية",
            [
                Rule("R-ADV-HIGH", "سلفة عالية → dual approval", 10,
                    """{"all":[{"field":"amount","operator":"GreaterThan","value":"50000"}]}""",
                    """[{"effect":"RequireDualApproval"}]""")
            ],
            [Field("amount", "decimal", "المبلغ"), Field("action", "string", "الإجراء")]),
        new(PolicyDomains.ContractsHire, "HireWizard", "توظيف — workflow",
            [
                Rule("R-HIRE-START", "بدء توظيف", 10,
                    """{"all":[{"field":"action","operator":"Equals","value":"Start"}]}""",
                    """[{"effect":"TriggerWorkflow","params":{"workflowCode":"HIRE-WIZARD"}}]""")
            ],
            [Field("action", "string", "الإجراء")]),
        new(PolicyDomains.ContractsSeparation, "Separation", "انفصال — workflow",
            [
                Rule("R-SEP-START", "بدء انفصال", 10,
                    """{"all":[{"field":"action","operator":"Equals","value":"Start"}]}""",
                    """[{"effect":"TriggerWorkflow","params":{"workflowCode":"SEPARATION-WIZARD"}}]""")
            ],
            [Field("action", "string", "الإجراء")]),
        new(PolicyDomains.ContractsContract, "Contract", "عقد — تجديد",
            [
                Rule("R-CON-RENEW", "تجديد عقد", 10,
                    """{"all":[{"field":"action","operator":"Equals","value":"Renew"}]}""",
                    """[{"effect":"RequireApproval"}]""")
            ],
            [Field("action", "string", "الإجراء")]),
        new(PolicyDomains.AssetsProcurement, "SupplyOrder", "توريد — استلام",
            [
                Rule("R-RCV-FULFIL", "استلام لطلب منفّذ فقط", 10,
                    """{"all":[{"field":"orderStatus","operator":"Equals","value":"Fulfilled"}]}""",
                    """[{"effect":"Allow"}]"""),
                Rule("R-RCV-DENY", "رفض استلام غير منفّذ", 5,
                    """{"all":[{"field":"orderStatus","operator":"NotEquals","value":"Fulfilled"}]}""",
                    """[{"effect":"Deny","params":{"reason":"طلب غير منفّذ"}}]""")
            ],
            [Field("orderStatus", "string", "حالة الطلب"), Field("action", "string", "الإجراء")]),
        new(PolicyDomains.MovementsTransfer, "Transfer", "نقل — workflow",
            [
                Rule("R-TRF", "نقل → workflow", 10,
                    """{"all":[{"field":"action","operator":"Equals","value":"Submit"}]}""",
                    """[{"effect":"TriggerWorkflow","params":{"workflowCode":"TRANSFER-APPROVAL"}}]""")
            ],
            [Field("action", "string", "الإجراء")]),
        new(PolicyDomains.MovementsWorkforce, "WorkforceGap", "فجوة قوى", [], [Field("gapCount", "int", "عدد الفجوات")]),
        new(PolicyDomains.CoreEmployment, "EmploymentRecord", "حالة توظيف", [], [Field("status", "string", "الحالة")]),
        new(PolicyDomains.HealthInsurance, "InsuranceCard", "تأمين",
            [
                Rule("R-INS-MASK", "خصوصية → mask", 10,
                    """{"all":[{"field":"isSensitive","operator":"Equals","value":"true"}]}""",
                    """[{"effect":"RequirePrivacyMask"}]""")
            ],
            [Field("isSensitive", "bool", "حساس"), Field("action", "string", "الإجراء")]),
        new(PolicyDomains.ComplianceDisciplinary, "DisciplinaryAction", "تأديب",
            [
                Rule("R-DISC", "إجراء تأديبي → workflow", 10,
                    """{"all":[{"field":"action","operator":"Equals","value":"Create"}]}""",
                    """[{"effect":"TriggerWorkflow","params":{"workflowCode":"DISCIPLINARY-APPROVAL"}}]""")
            ],
            [Field("action", "string", "الإجراء")]),
        new(PolicyDomains.PayrollPeriod, "PayrollPeriod", "فترة رواتب",
            [
                Rule("R-PAY-CLOSE", "إغلاق فترة → dual", 10,
                    """{"all":[{"field":"action","operator":"Equals","value":"Close"}]}""",
                    """[{"effect":"RequireDualApproval"}]""")
            ],
            [Field("action", "string", "الإجراء")]),
        new(PolicyDomains.ITTickets, "Ticket", "تذاكر IT",
            [
                Rule("R-TKT", "تذكرة → workflow", 10,
                    """{"all":[{"field":"action","operator":"Equals","value":"Open"}]}""",
                    """[{"effect":"TriggerWorkflow","params":{"workflowCode":"IT-TICKET"}}]""")
            ],
            [Field("priority", "string", "الأولوية"), Field("action", "string", "الإجراء")]),
        new(PolicyDomains.ArchiveRetention, "ArchiveRecord", "أرشفة",
            [
                Rule("R-ARC", "احتفاظ → evidence", 10,
                    """{"all":[{"field":"requiresRetention","operator":"Equals","value":"true"}]}""",
                    """[{"effect":"RequireEvidence"}]"""),
                .. BaselineRules(PolicyDomains.ArchiveRetention)
            ],
            [Field("requiresRetention", "bool", "احتفاظ"), Field("action", "string", "الإجراء"), .. BaselineFields(PolicyDomains.ArchiveRetention)]),
        new(PolicyDomains.TrainingLearning, "TrainingCourse", "تدريب",
            [
                Rule("R-TRN", "إكمال → checklist", 10,
                    """{"all":[{"field":"action","operator":"Equals","value":"Complete"}]}""",
                    """[{"effect":"BlockUntilChecklist"}]""")
            ],
            [Field("action", "string", "الإجراء")]),
        new(PolicyDomains.AnalyticsEtl, "EtlJob", "ETL",
            [
                Rule("R-ETL", "ETL production → approval", 10,
                    """{"all":[{"field":"environment","operator":"Equals","value":"Production"}]}""",
                    """[{"effect":"RequireApproval"}]""")
            ],
            [Field("environment", "string", "البيئة"), Field("action", "string", "الإجراء")])
    ];

    /// <summary>The thirteen bindings of the old seeder: eleven pinned to the first definition of the code, two unpinned.</summary>
    public static readonly IReadOnlyList<(string Domain, string WorkflowCode, bool Pinned)> Bindings =
    [
        (PolicyDomains.AssetsCustody, "ASSET-ASSIGN-APPROVAL", true),
        (PolicyDomains.AssetsCustody, "ASSET-LOSS-APPROVAL", true),
        (PolicyDomains.IDCardLifecycle, "ID-CARD-ISSUANCE", true),
        (PolicyDomains.ContractsHire, "HIRE-WIZARD", true),
        (PolicyDomains.ContractsSeparation, "SEPARATION-WIZARD", true),
        (PolicyDomains.MovementsTransfer, "TRANSFER-APPROVAL", true),
        (PolicyDomains.CoreRecruitment, "RECRUITMENT-REQUISITION-APPROVAL", true),
        (PolicyDomains.CoreRecruitment, "RECRUITMENT-OFFER-APPROVAL", true),
        (PolicyDomains.CoreRecruitment, "RECRUITMENT-OFFER-OVERRIDE-APPROVAL", true),
        (PolicyDomains.ComplianceDisciplinary, "DISCIPLINARY-APPROVAL", true),
        (PolicyDomains.ITTickets, "IT-TICKET", true),
        (PolicyDomains.CoreRecruitment, "RECRUITMENT-SELECTION-COMMITTEE", false),
        (PolicyDomains.CoreRecruitment, "RECRUITMENT-APPEAL-REVIEW", false)
    ];

    /// <summary>The change sets the demo server had applied (all NoOp: the rules were already there), by set and domain.</summary>
    public static readonly IReadOnlyList<(PolicySeedChangeSet Set, string Domain)> AppliedSets =
    [
        (PolicySeedChangeSets.Baseline, PolicyDomains.ContractsLeave),
        (PolicySeedChangeSets.Baseline, PolicyDomains.CoreRecruitment),
        (PolicySeedChangeSets.Baseline, PolicyDomains.ArchiveRetention),
        (PolicySeedChangeSets.Baseline, PolicyDomains.AssetsCustody),
        (PolicySeedChangeSets.RetireRecruitmentHire, PolicyDomains.CoreRecruitment)
    ];

    /// <summary>
    /// Writes the data in the national tenant. The workflow definitions must be there first (WorkflowPackSeeder): a pinned
    /// binding points at the first definition of its code, as the old seeder made it. With <paramref name="recordAppliedSets"/>
    /// the five applications of <see cref="AppliedSets"/> are recorded too, as the server has them.
    /// </summary>
    public static async Task SeedAsync(HRCoreDbContext db, bool recordAppliedSets = true, CancellationToken ct = default)
    {
        var tenant = TenantDefaults.DefaultTenantId;
        var templates = new Dictionary<string, PolicyTemplate>(StringComparer.Ordinal);
        foreach (var domain in Domains)
        {
            var template = new PolicyTemplate
            {
                TenantId = tenant,
                Domain = domain.Domain,
                Name = domain.Name,
                SubjectType = domain.SubjectType,
                Scope = PolicyTemplateScope.National,
                Description = $"seed PROD001ab — {domain.Domain}"
            };
            db.PolicyTemplates.Add(template);
            templates[domain.Domain] = template;
            db.PolicyFactsSchemas.Add(new PolicyFactsSchemaEntity
            {
                TenantId = tenant,
                Domain = domain.Domain,
                SubjectType = domain.SubjectType,
                FieldsJson = JsonSerializer.Serialize(domain.Fields.Select(f => new PolicyFieldSchema { Name = f.Name, Type = f.Type, DisplayNameAr = f.DisplayAr }).ToList())
            });

            await PolicySeedVersionWriter.AddAsync(db,
                new PolicyTemplateVersion { PolicyTemplate = template, TenantId = tenant, VersionNumber = 1, EffectiveFrom = DateTime.UtcNow.AddYears(-1) },
                domain.Rules.Select(r => new PolicyRule { TenantId = tenant, RuleKey = r.Key, Name = r.Name, Priority = r.Priority, WhenJson = r.When, ThenJson = r.Then }).ToList(),
                PolicyVersionStatus.Published,
                new PolicyPublication { TenantId = tenant, PublishedAt = DateTime.UtcNow.AddYears(-1), EvidenceRef = $"seed-{domain.Domain}", Kind = PolicyPublicationKind.SystemSeed },
                ct);
        }

        foreach (var binding in Bindings)
        {
            var definition = binding.Pinned
                ? await db.WorkflowDefinitions.AsNoTracking().OrderBy(d => d.Version).FirstOrDefaultAsync(d => d.Code == binding.WorkflowCode, ct)
                : null;
            db.PolicyWorkflowBindings.Add(new PolicyWorkflowBinding
            {
                TenantId = tenant,
                Domain = binding.Domain,
                WorkflowCode = binding.WorkflowCode,
                WorkflowDefinitionId = definition?.Id
            });
        }

        if (recordAppliedSets)
        {
            foreach (var (set, domain) in AppliedSets)
            {
                db.PolicySeedApplications.Add(new PolicySeedApplication
                {
                    TenantId = tenant,
                    ChangeSetId = set.Id,
                    PolicyTemplateId = templates[domain].Id,
                    Outcome = PolicySeedChangeSetApplier.OutcomeNoOp,
                    Digest = set.Digest(domain),
                    AppliedAt = DateTime.UtcNow.AddDays(-2)
                });
            }
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }
}
