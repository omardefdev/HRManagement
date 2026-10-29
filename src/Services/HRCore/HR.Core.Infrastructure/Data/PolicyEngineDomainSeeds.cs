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

        // PLAN-25 (G-14): no R-REC-HIRE — the hire is governed by the offer approval (existing databases: see
        // DeactivateRuleIfPresentAsync in SeedIncrementalRulesAsync).
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

    /// <summary>SEED-001 W4: idempotent national rule patches.</summary>
    public static async Task SeedIncrementalRulesAsync(HRCoreDbContext db, CancellationToken ct = default)
    {
        await AddRuleIfMissingAsync(db, PolicyDomains.ContractsLeave, "R-LEAVE-SELF",
            "منع الموافقة الذاتية", 5,
            """{"all":[{"field":"isSelfApproval","operator":"Equals","value":"true"}]}""",
            """[{"effect":"Deny","params":{"reason":"لا يمكن الموافقة على إجازتك"}}]""", ct);

        await AddRuleIfMissingAsync(db, PolicyDomains.ContractsLeave, "R-LEAVE-MAX",
            "حد أقصى للإجازة", 8,
            """{"all":[{"field":"durationDays","operator":"GreaterThan","value":"30"}]}""",
            """[{"effect":"Deny","params":{"reason":"تتجاوز الحد الأقصى الوطني"}}]""", ct);

        await AddRuleIfMissingAsync(db, PolicyDomains.CoreRecruitment, "R-REC-ORG",
            "توظيف يتطلب نطاق تنظيمي", 5,
            """{"all":[{"field":"action","operator":"Equals","value":"Hire"},{"field":"hasOrgScope","operator":"Equals","value":"false"}]}""",
            """[{"effect":"Deny","params":{"reason":"يجب تحديد الوحدة التنظيمية"}}]""", ct);

        await AddRuleIfMissingAsync(db, PolicyDomains.CoreRecruitment, "R-REQ-SUBMIT",
            "تقديم طلب شاغر → workflow", 10,
            """{"all":[{"field":"action","operator":"Equals","value":"SubmitForApproval"}]}""",
            """[{"effect":"TriggerWorkflow","params":{"workflowCode":"RECRUITMENT-REQUISITION-APPROVAL"}}]""", ct);

        await AddRuleIfMissingAsync(db, PolicyDomains.CoreRecruitment, "R-REQ-PUBLISH-DENY",
            "رفض نشر طلب غير معتمد", 5,
            """{"any":[{"all":[{"field":"action","operator":"Equals","value":"Publish"},{"field":"requisitionStatus","operator":"NotEquals","value":"Approved"}]},{"all":[{"field":"action","operator":"Equals","value":"Publish"},{"field":"positionVacant","operator":"Equals","value":"false"}]}]}""",
            """[{"effect":"Deny","params":{"reason":"لا يمكن النشر إلا لطلب معتمد على منصب شاغر"}}]""", ct);

        await AddRuleIfMissingAsync(db, PolicyDomains.CoreRecruitment, "R-REQ-PUBLISH",
            "نشر طلب معتمد", 10,
            """{"all":[{"field":"action","operator":"Equals","value":"Publish"},{"field":"requisitionStatus","operator":"Equals","value":"Approved"},{"field":"positionVacant","operator":"Equals","value":"true"}]}""",
            """[{"effect":"Allow"}]""", ct);

        await EnsureFactsFieldAsync(db, PolicyDomains.CoreRecruitment, "requisitionStatus", "string", "حالة الطلب", ct);
        await EnsureFactsFieldAsync(db, PolicyDomains.CoreRecruitment, "positionVacant", "bool", "منصب شاغر", ct);
        await EnsureFactsFieldAsync(db, PolicyDomains.CoreRecruitment, "fundedPositionId", "string", "معرف المنصب الممول", ct);

        await AddRuleIfMissingAsync(db, PolicyDomains.ArchiveRetention, "R-ARC-HOLD",
            "حجز قانوني → منع الحذف", 5,
            """{"all":[{"field":"legalHold","operator":"Equals","value":"true"},{"field":"action","operator":"Equals","value":"Delete"}]}""",
            """[{"effect":"Deny","params":{"reason":"سجل محجوز قانونياً"}}]""", ct);

        await AddRuleIfMissingAsync(db, PolicyDomains.AssetsCustody, "R-CLASSIFY-HIGH-VALUE",
            "تصنيف — قيمة عالية → مميز", 5,
            """{"all":[{"field":"action","operator":"Equals","value":"Classify"},{"field":"value","operator":"GreaterThan","value":"10000"}]}""",
            """[{"effect":"SetField","params":{"field":"isPrivileged","value":"true"}}]""", ct);

        await AddRuleIfMissingAsync(db, PolicyDomains.AssetsCustody, "R-CLASSIFY-CAPITAL",
            "تصنيف — أصول ثابتة → مميز", 6,
            """{"all":[{"field":"action","operator":"Equals","value":"Classify"},{"field":"category","operator":"Equals","value":"أصول ثابتة"},{"field":"value","operator":"GreaterThan","value":"5000"}]}""",
            """[{"effect":"SetField","params":{"field":"isPrivileged","value":"true"}}]""", ct);

        await EnsureFactsFieldAsync(db, PolicyDomains.ContractsLeave, "isSelfApproval", "bool", "موافقة ذاتية", ct);
        await EnsureFactsFieldAsync(db, PolicyDomains.CoreRecruitment, "hasOrgScope", "bool", "نطاق تنظيمي", ct);
        await EnsureFactsFieldAsync(db, PolicyDomains.CoreRecruitment, "hasAcceptedOffer", "bool", "عرض مقبول", ct);
        await EnsureFactsFieldAsync(db, PolicyDomains.CoreRecruitment, "coiDeclared", "bool", "إقرار COI", ct);
        await EnsureFactsFieldAsync(db, PolicyDomains.CoreRecruitment, "rankAssigned", "bool", "ترتيب معيّن", ct);

        // HCM-027: merit override offers. Rules are evaluated by ascending priority and the first
        // TriggerWorkflow effect wins, so priority 9 beats R-OFF-ISSUE (10) only when isMeritOverride is true.
        await EnsureFactsFieldAsync(db, PolicyDomains.CoreRecruitment, "recruitmentApplicationId", "string", "معرف طلب التوظيف", ct);
        await EnsureFactsFieldAsync(db, PolicyDomains.CoreRecruitment, "isMeritOverride", "bool", "تجاوز الجدارة", ct);
        await AddRuleIfMissingAsync(db, PolicyDomains.CoreRecruitment, "R-OFF-OVERRIDE",
            "إصدار عرض استثنائي (تجاوز الجدارة) → workflow", 9,
            """{"all":[{"field":"action","operator":"Equals","value":"Issue"},{"field":"isMeritOverride","operator":"Equals","value":"true"}]}""",
            """[{"effect":"TriggerWorkflow","params":{"workflowCode":"RECRUITMENT-OFFER-OVERRIDE-APPROVAL"}}]""", ct);

        // HCM-027 (E1): the exception trigger is generalised to grade deviation and unverified salary.
        // A separate rule (instead of rewriting R-OFF-OVERRIDE) so databases seeded by session 1 pick it up.
        await EnsureFactsFieldAsync(db, PolicyDomains.CoreRecruitment, "isExceptionOffer", "bool", "عرض استثنائي", ct);
        await AddRuleIfMissingAsync(db, PolicyDomains.CoreRecruitment, "R-OFF-EXCEPTION",
            "إصدار عرض استثنائي (جدارة/درجة/راتب غير متحقق) → workflow", 8,
            """{"all":[{"field":"action","operator":"Equals","value":"Issue"},{"field":"isExceptionOffer","operator":"Equals","value":"true"}]}""",
            """[{"effect":"TriggerWorkflow","params":{"workflowCode":"RECRUITMENT-OFFER-OVERRIDE-APPROVAL"}}]""", ct);

        await AddRuleIfMissingAsync(db, PolicyDomains.CoreRecruitment, "R-OFF-ISSUE",
            "إصدار عرض → workflow", 10,
            """{"all":[{"field":"action","operator":"Equals","value":"Issue"}]}""",
            """[{"effect":"TriggerWorkflow","params":{"workflowCode":"RECRUITMENT-OFFER-APPROVAL"}}]""", ct);

        await AddRuleIfMissingAsync(db, PolicyDomains.CoreRecruitment, "R-COMMITTEE-SUBMIT",
            "تقديم لجنة اختيار → workflow", 10,
            """{"all":[{"field":"action","operator":"Equals","value":"SubmitCommittee"}]}""",
            """[{"effect":"TriggerWorkflow","params":{"workflowCode":"RECRUITMENT-SELECTION-COMMITTEE"}}]""", ct);

        await AddRuleIfMissingAsync(db, PolicyDomains.CoreRecruitment, "R-APPEAL-FILE",
            "تقديم استئناف → workflow", 10,
            """{"all":[{"field":"action","operator":"Equals","value":"FileAppeal"}]}""",
            """[{"effect":"TriggerWorkflow","params":{"workflowCode":"RECRUITMENT-APPEAL-REVIEW"}}]""", ct);

        await AddRuleIfMissingAsync(db, PolicyDomains.CoreRecruitment, "R-REC-HIRE-OFFER",
            "تعيين يتطلب عرضاً مقبولاً", 6,
            """{"all":[{"field":"action","operator":"Equals","value":"Hire"},{"field":"hasAcceptedOffer","operator":"Equals","value":"false"}]}""",
            """[{"effect":"Deny","params":{"reason":"يجب قبول خطاب العرض قبل التعيين"}}]""", ct);

        // PLAN-25 (G-14): the orphan RECRUITMENT-HIRE trigger is retired; R-REC-ORG and R-REC-HIRE-OFFER stay.
        await DeactivateRuleIfPresentAsync(db, "R-REC-HIRE", "توظيف → workflow (أُلغيت: التعيين محكوم باعتماد العرض — PLAN-25)", ct);
        await EnsureFactsFieldAsync(db, PolicyDomains.ArchiveRetention, "legalHold", "bool", "حجز قانوني", ct);
    }

    /// <summary>
    /// PLAN-25 (G-14): <see cref="AddRuleIfMissingAsync"/> never updates a rule, so a retired rule is neutralised in place:
    /// it matches no action and has no effect. The row stays (rule keys are never reused). Idempotent.
    /// </summary>
    public static async Task DeactivateRuleIfPresentAsync(HRCoreDbContext db, string ruleKey, string reason, CancellationToken ct = default)
    {
        var rules = await db.PolicyRules.Where(r => r.RuleKey == ruleKey).ToListAsync(ct);
        var changed = false;
        foreach (var rule in rules.Where(r => r.ThenJson != DeactivatedThenJson || r.WhenJson != DeactivatedWhenJson))
        {
            rule.WhenJson = DeactivatedWhenJson;
            rule.ThenJson = DeactivatedThenJson;
            rule.Name = Truncate($"{DeactivatedNamePrefix}{reason}", 200);
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync(ct);
    }

    public const string DeactivatedNamePrefix = "[معطّلة] ";
    public const string DeactivatedWhenJson = """{"all":[{"field":"action","operator":"Equals","value":"__deactivated__"}]}""";
    public const string DeactivatedThenJson = "[]";

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static async Task AddRuleIfMissingAsync(
        HRCoreDbContext db,
        string domain,
        string ruleKey,
        string name,
        int priority,
        string whenJson,
        string thenJson,
        CancellationToken ct)
    {
        if (await db.PolicyRules.AnyAsync(r => r.RuleKey == ruleKey, ct))
            return;

        var version = await db.PolicyTemplateVersions.AsNoTracking()
            .Include(v => v.PolicyTemplate)
            .Where(v => v.PolicyTemplate!.Domain == domain)
            .Where(v => v.Status == PolicyVersionStatus.Published)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);
        if (version is null)
            return;

        db.PolicyRules.Add(new PolicyRule
        {
            PolicyTemplateVersionId = version.Id,
            RuleKey = ruleKey,
            Name = name,
            Priority = priority,
            WhenJson = whenJson,
            ThenJson = thenJson
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureFactsFieldAsync(
        HRCoreDbContext db,
        string domain,
        string fieldName,
        string fieldType,
        string displayAr,
        CancellationToken ct)
    {
        var schema = await db.PolicyFactsSchemas.FirstOrDefaultAsync(s => s.Domain == domain, ct);
        if (schema is null)
            return;

        var fields = JsonSerializer.Deserialize<List<PolicyFieldSchema>>(schema.FieldsJson) ?? [];
        if (fields.Any(f => f.Name == fieldName))
            return;

        fields.Add(new PolicyFieldSchema { Name = fieldName, Type = fieldType, DisplayNameAr = displayAr });
        schema.FieldsJson = JsonSerializer.Serialize(fields);
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

        var version = new PolicyTemplateVersion
        {
            PolicyTemplate = template,
            VersionNumber = 1,
            Status = PolicyVersionStatus.Published,
            EffectiveFrom = DateTime.UtcNow.AddYears(-1)
        };
        db.PolicyTemplateVersions.Add(version);

        foreach (var r in rules)
        {
            db.PolicyRules.Add(new PolicyRule
            {
                PolicyTemplateVersion = version,
                RuleKey = r.Key,
                Name = r.Name,
                Priority = r.Priority,
                WhenJson = r.When,
                ThenJson = r.Then
            });
        }

        db.PolicyPublications.Add(new PolicyPublication
        {
            PolicyTemplateVersion = version,
            PublishedAt = DateTime.UtcNow.AddYears(-1),
            EvidenceRef = $"seed-{domain}"
        });

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
    }

    private static (string Key, string Name, int Priority, string When, string Then) Rule(
        string key, string name, int priority, string when, string then) => (key, name, priority, when, then);

    private static PolicyFieldSchema Field(string name, string type, string displayAr) =>
        new() { Name = name, Type = type, DisplayNameAr = displayAr };
}
