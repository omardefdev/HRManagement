using System.Text.Json;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Data;

/// <summary>National policy seeds for all registered domains (PROD-001ab).</summary>
public static class PolicyEngineDomainSeeds
{
    public static async Task SeedAllDomainsAsync(HRCoreDbContext db, CancellationToken ct = default)
    {
        await SeedDomainIfMissingAsync(db, PolicyDomains.IDCardLifecycle, "CardRequest", "بطاقة تعريف — دورة الحياة", ct,
            [
                Rule("R-ID-PROCESS", "معالجة → workflow", 10,
                    """{"all":[{"field":"action","operator":"Equals","value":"Process"}]}""",
                    """[{"effect":"TriggerWorkflow","params":{"workflowCode":"ID-CARD-ISSUANCE"}}]""")
            ],
            [Field("purpose", "string", "الغرض"), Field("action", "string", "الإجراء")]);

        await SeedDomainIfMissingAsync(db, PolicyDomains.ContractsLeave, "LeaveRequest", "إجازات — وطنية", ct,
            [
                Rule("R-LEAVE-LONG", "إجازة طويلة → موافقة", 10,
                    """{"all":[{"field":"durationDays","operator":"GreaterThan","value":"14"}]}""",
                    """[{"effect":"RequireApproval"}]""")
            ],
            [Field("durationDays", "int", "المدة"), Field("leaveType", "string", "النوع"), Field("action", "string", "الإجراء")]);

        await SeedDomainIfMissingAsync(db, PolicyDomains.ContractsAdvance, "AdvanceRequest", "سلف — وطنية", ct,
            [
                Rule("R-ADV-HIGH", "سلفة عالية → dual approval", 10,
                    """{"all":[{"field":"amount","operator":"GreaterThan","value":"50000"}]}""",
                    """[{"effect":"RequireDualApproval"}]""")
            ],
            [Field("amount", "decimal", "المبلغ"), Field("action", "string", "الإجراء")]);

        await SeedDomainIfMissingAsync(db, PolicyDomains.ContractsHire, "HireWizard", "توظيف — workflow", ct,
            [
                Rule("R-HIRE-START", "بدء توظيف", 10,
                    """{"all":[{"field":"action","operator":"Equals","value":"Start"}]}""",
                    """[{"effect":"TriggerWorkflow","params":{"workflowCode":"HIRE-WIZARD"}}]""")
            ],
            [Field("action", "string", "الإجراء")]);

        await SeedDomainIfMissingAsync(db, PolicyDomains.ContractsSeparation, "Separation", "انفصال — workflow", ct,
            [
                Rule("R-SEP-START", "بدء انفصال", 10,
                    """{"all":[{"field":"action","operator":"Equals","value":"Start"}]}""",
                    """[{"effect":"TriggerWorkflow","params":{"workflowCode":"SEPARATION-WIZARD"}}]""")
            ],
            [Field("action", "string", "الإجراء")]);

        await SeedDomainIfMissingAsync(db, PolicyDomains.ContractsContract, "Contract", "عقد — تجديد", ct,
            [
                Rule("R-CON-RENEW", "تجديد عقد", 10,
                    """{"all":[{"field":"action","operator":"Equals","value":"Renew"}]}""",
                    """[{"effect":"RequireApproval"}]""")
            ],
            [Field("action", "string", "الإجراء")]);

        await SeedDomainIfMissingAsync(db, PolicyDomains.AssetsProcurement, "SupplyOrder", "توريد — استلام", ct,
            [
                Rule("R-RCV-FULFIL", "استلام لطلب منفّذ فقط", 10,
                    """{"all":[{"field":"orderStatus","operator":"Equals","value":"Fulfilled"}]}""",
                    """[{"effect":"Allow"}]"""),
                Rule("R-RCV-DENY", "رفض استلام غير منفّذ", 5,
                    """{"all":[{"field":"orderStatus","operator":"NotEquals","value":"Fulfilled"}]}""",
                    """[{"effect":"Deny","params":{"reason":"طلب غير منفّذ"}}]""")
            ],
            [Field("orderStatus", "string", "حالة الطلب"), Field("action", "string", "الإجراء")]);

        await SeedDomainIfMissingAsync(db, PolicyDomains.MovementsTransfer, "Transfer", "نقل — workflow", ct,
            [
                Rule("R-TRF", "نقل → workflow", 10,
                    """{"all":[{"field":"action","operator":"Equals","value":"Submit"}]}""",
                    """[{"effect":"TriggerWorkflow","params":{"workflowCode":"TRANSFER-APPROVAL"}}]""")
            ],
            [Field("action", "string", "الإجراء")]);

        await SeedDomainIfMissingAsync(db, PolicyDomains.MovementsWorkforce, "WorkforceGap", "فجوة قوى", ct,
            [],
            [Field("gapCount", "int", "عدد الفجوات")]);

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

        await SeedDomainIfMissingAsync(db, PolicyDomains.CoreEmployment, "EmploymentRecord", "حالة توظيف", ct,
            [],
            [Field("status", "string", "الحالة")]);

        await SeedDomainIfMissingAsync(db, PolicyDomains.HealthInsurance, "InsuranceCard", "تأمين", ct,
            [
                Rule("R-INS-MASK", "خصوصية → mask", 10,
                    """{"all":[{"field":"isSensitive","operator":"Equals","value":"true"}]}""",
                    """[{"effect":"RequirePrivacyMask"}]""")
            ],
            [Field("isSensitive", "bool", "حساس"), Field("action", "string", "الإجراء")]);

        await SeedDomainIfMissingAsync(db, PolicyDomains.ComplianceDisciplinary, "DisciplinaryAction", "تأديب", ct,
            [
                Rule("R-DISC", "إجراء تأديبي → workflow", 10,
                    """{"all":[{"field":"action","operator":"Equals","value":"Create"}]}""",
                    """[{"effect":"TriggerWorkflow","params":{"workflowCode":"DISCIPLINARY-APPROVAL"}}]""")
            ],
            [Field("action", "string", "الإجراء")]);

        await SeedDomainIfMissingAsync(db, PolicyDomains.PayrollPeriod, "PayrollPeriod", "فترة رواتب", ct,
            [
                Rule("R-PAY-CLOSE", "إغلاق فترة → dual", 10,
                    """{"all":[{"field":"action","operator":"Equals","value":"Close"}]}""",
                    """[{"effect":"RequireDualApproval"}]""")
            ],
            [Field("action", "string", "الإجراء")]);

        await SeedDomainIfMissingAsync(db, PolicyDomains.ITTickets, "Ticket", "تذاكر IT", ct,
            [
                Rule("R-TKT", "تذكرة → workflow", 10,
                    """{"all":[{"field":"action","operator":"Equals","value":"Open"}]}""",
                    """[{"effect":"TriggerWorkflow","params":{"workflowCode":"IT-TICKET"}}]""")
            ],
            [Field("priority", "string", "الأولوية"), Field("action", "string", "الإجراء")]);

        await SeedDomainIfMissingAsync(db, PolicyDomains.ArchiveRetention, "ArchiveRecord", "أرشفة", ct,
            [
                Rule("R-ARC", "احتفاظ → evidence", 10,
                    """{"all":[{"field":"requiresRetention","operator":"Equals","value":"true"}]}""",
                    """[{"effect":"RequireEvidence"}]""")
            ],
            [Field("requiresRetention", "bool", "احتفاظ"), Field("action", "string", "الإجراء")]);

        await SeedDomainIfMissingAsync(db, PolicyDomains.TrainingLearning, "TrainingCourse", "تدريب", ct,
            [
                Rule("R-TRN", "إكمال → checklist", 10,
                    """{"all":[{"field":"action","operator":"Equals","value":"Complete"}]}""",
                    """[{"effect":"BlockUntilChecklist"}]""")
            ],
            [Field("action", "string", "الإجراء")]);

        await SeedDomainIfMissingAsync(db, PolicyDomains.AnalyticsEtl, "EtlJob", "ETL", ct,
            [
                Rule("R-ETL", "ETL production → approval", 10,
                    """{"all":[{"field":"environment","operator":"Equals","value":"Production"}]}""",
                    """[{"effect":"RequireApproval"}]""")
            ],
            [Field("environment", "string", "البيئة"), Field("action", "string", "الإجراء")]);

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
