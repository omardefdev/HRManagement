using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HRManagement.SharedKernel.Policy;

namespace HR.Core.Infrastructure.Data;

public enum PolicySeedOperationKind
{
    /// <summary>Adds the rule when the effective version has no rule with its key; no effect otherwise.</summary>
    AddRule,
    /// <summary>Replaces the rule with its key when it differs; no effect when it is absent or the same.</summary>
    ReplaceRule,
    /// <summary>Removes the rule with its key from the new version; no effect when it is absent.</summary>
    RetireRule,
    /// <summary>Adds a field to the domain's facts schema (the schema is not versioned).</summary>
    EnsureFactsField,
    /// <summary>
    /// PLAN-49 (D-POL-1): retires the national template of a domain the catalog puts out of scope: one final published version
    /// with no rules and the operation's reason, and the template marked retired. No effect on a governed domain, and none where
    /// the template does not exist (a new database seeds none).
    /// </summary>
    RetireDomain
}

/// <summary>One operation of a seed change set, on the national template of <see cref="Domain"/>.</summary>
public sealed record PolicySeedOperation(
    PolicySeedOperationKind Kind,
    string Domain,
    string? RuleKey = null,
    string? Name = null,
    int Priority = 0,
    string? WhenJson = null,
    string? ThenJson = null,
    string? ReasonAr = null,
    string? FieldName = null,
    string? FieldType = null,
    string? FieldDisplayAr = null);

/// <summary>
/// PLAN-47 (§4, D-POL-11): a recorded change of the seeded policies. Applied once per template, as a new version published by
/// SystemSeed (or a draft for an approver with PolicyEngine:Seeds:RequireApproval=true). Never edited once applied: its digest is
/// stored, and an applied set whose digest changed fails the boot. A change is a new set with a new id.
/// </summary>
public sealed record PolicySeedChangeSet(string Id, IReadOnlyList<PolicySeedOperation> Operations)
{
    public IEnumerable<string> Domains => Operations.Select(o => o.Domain).Distinct(StringComparer.Ordinal);

    public IReadOnlyList<PolicySeedOperation> For(string domain) => Operations.Where(o => o.Domain == domain).ToList();

    /// <summary>SHA-256 (lower-case hex) of the set's operations on one domain, in their order.</summary>
    public string Digest(string domain)
    {
        var canonical = JsonSerializer.Serialize(For(domain).Select(o => new object?[]
        {
            o.Kind.ToString(), o.Domain, o.RuleKey, o.Name, o.Priority, o.WhenJson, o.ThenJson, o.ReasonAr, o.FieldName, o.FieldType, o.FieldDisplayAr
        }));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Id}\n{domain}\n{canonical}"))).ToLowerInvariant();
    }
}

/// <summary>PLAN-47: every seed change set, in the order they apply. Append only.</summary>
public static class PolicySeedChangeSets
{
    public const string BaselineId = "POL-2026-10-30-BASELINE";
    public const string RetireRecruitmentHireId = "POL-2026-10-30-RETIRE-R-REC-HIRE";
    public const string ScopeRetireId = "POL-049-SCOPE-RETIRE";
    public const string AssetsActionId = "POL-049-ASSETS-ACTION";
    public const string IdCardOrphanId = "POL-049-IDCARD-ORPHAN";
    public const string LeaveId = "POL-049-LEAVE";

    private static PolicySeedOperation Add(string domain, string key, string name, int priority, string when, string then) =>
        new(PolicySeedOperationKind.AddRule, domain, key, name, priority, when, then);

    private static PolicySeedOperation Replace(string domain, PolicySeedRule rule) =>
        new(PolicySeedOperationKind.ReplaceRule, domain, rule.Key, rule.Name, rule.Priority, rule.When, rule.Then);

    private static PolicySeedOperation Retire(string domain, string key, string reasonAr) =>
        new(PolicySeedOperationKind.RetireRule, domain, RuleKey: key, ReasonAr: reasonAr);

    private static PolicySeedOperation RetireDomain(string domain, string reasonAr) =>
        new(PolicySeedOperationKind.RetireDomain, domain, ReasonAr: reasonAr);

    private static PolicySeedOperation Field(string domain, string name, string type, string displayAr) =>
        new(PolicySeedOperationKind.EnsureFactsField, domain, FieldName: name, FieldType: type, FieldDisplayAr: displayAr);

    /// <summary>
    /// What the start-up seed patches added in place to published versions before PLAN-47. On a database
    /// where they did, every operation finds its rule and the set is NoOp; on a new database it publishes v2 of each domain it adds
    /// to. It adds no rule that fires a retired workflow (PLAN-46).
    /// </summary>
    public static readonly PolicySeedChangeSet Baseline = new(BaselineId,
    [
        Field(PolicyDomains.ContractsLeave, "isSelfApproval", "bool", "موافقة ذاتية"),
        Add(PolicyDomains.ContractsLeave, "R-LEAVE-SELF", "منع الموافقة الذاتية", 5,
            """{"all":[{"field":"isSelfApproval","operator":"Equals","value":"true"}]}""",
            """[{"effect":"Deny","params":{"reason":"لا يمكن الموافقة على إجازتك"}}]"""),
        Add(PolicyDomains.ContractsLeave, "R-LEAVE-MAX", "حد أقصى للإجازة", 8,
            """{"all":[{"field":"durationDays","operator":"GreaterThan","value":"30"}]}""",
            """[{"effect":"Deny","params":{"reason":"تتجاوز الحد الأقصى الوطني"}}]"""),

        Field(PolicyDomains.CoreRecruitment, "requisitionStatus", "string", "حالة الطلب"),
        Field(PolicyDomains.CoreRecruitment, "positionVacant", "bool", "منصب شاغر"),
        Field(PolicyDomains.CoreRecruitment, "fundedPositionId", "string", "معرف المنصب الممول"),
        Field(PolicyDomains.CoreRecruitment, "hasOrgScope", "bool", "نطاق تنظيمي"),
        Field(PolicyDomains.CoreRecruitment, "hasAcceptedOffer", "bool", "عرض مقبول"),
        Field(PolicyDomains.CoreRecruitment, "coiDeclared", "bool", "إقرار COI"),
        Field(PolicyDomains.CoreRecruitment, "rankAssigned", "bool", "ترتيب معيّن"),
        Field(PolicyDomains.CoreRecruitment, "recruitmentApplicationId", "string", "معرف طلب التوظيف"),
        Field(PolicyDomains.CoreRecruitment, "isMeritOverride", "bool", "تجاوز الجدارة"),
        Field(PolicyDomains.CoreRecruitment, "isExceptionOffer", "bool", "عرض استثنائي"),
        Add(PolicyDomains.CoreRecruitment, "R-REC-ORG", "توظيف يتطلب نطاق تنظيمي", 5,
            """{"all":[{"field":"action","operator":"Equals","value":"Hire"},{"field":"hasOrgScope","operator":"Equals","value":"false"}]}""",
            """[{"effect":"Deny","params":{"reason":"يجب تحديد الوحدة التنظيمية"}}]"""),
        Add(PolicyDomains.CoreRecruitment, "R-REQ-SUBMIT", "تقديم طلب شاغر → workflow", 10,
            """{"all":[{"field":"action","operator":"Equals","value":"SubmitForApproval"}]}""",
            """[{"effect":"TriggerWorkflow","params":{"workflowCode":"RECRUITMENT-REQUISITION-APPROVAL"}}]"""),
        Add(PolicyDomains.CoreRecruitment, "R-REQ-PUBLISH-DENY", "رفض نشر طلب غير معتمد", 5,
            """{"any":[{"all":[{"field":"action","operator":"Equals","value":"Publish"},{"field":"requisitionStatus","operator":"NotEquals","value":"Approved"}]},{"all":[{"field":"action","operator":"Equals","value":"Publish"},{"field":"positionVacant","operator":"Equals","value":"false"}]}]}""",
            """[{"effect":"Deny","params":{"reason":"لا يمكن النشر إلا لطلب معتمد على منصب شاغر"}}]"""),
        Add(PolicyDomains.CoreRecruitment, "R-REQ-PUBLISH", "نشر طلب معتمد", 10,
            """{"all":[{"field":"action","operator":"Equals","value":"Publish"},{"field":"requisitionStatus","operator":"Equals","value":"Approved"},{"field":"positionVacant","operator":"Equals","value":"true"}]}""",
            """[{"effect":"Allow"}]"""),
        // HCM-027: rules run by ascending priority and the first TriggerWorkflow wins: 8 and 9 beat R-OFF-ISSUE (10) when they match.
        Add(PolicyDomains.CoreRecruitment, "R-OFF-OVERRIDE", "إصدار عرض استثنائي (تجاوز الجدارة) → workflow", 9,
            """{"all":[{"field":"action","operator":"Equals","value":"Issue"},{"field":"isMeritOverride","operator":"Equals","value":"true"}]}""",
            """[{"effect":"TriggerWorkflow","params":{"workflowCode":"RECRUITMENT-OFFER-OVERRIDE-APPROVAL"}}]"""),
        Add(PolicyDomains.CoreRecruitment, "R-OFF-EXCEPTION", "إصدار عرض استثنائي (جدارة/درجة/راتب غير متحقق) → workflow", 8,
            """{"all":[{"field":"action","operator":"Equals","value":"Issue"},{"field":"isExceptionOffer","operator":"Equals","value":"true"}]}""",
            """[{"effect":"TriggerWorkflow","params":{"workflowCode":"RECRUITMENT-OFFER-OVERRIDE-APPROVAL"}}]"""),
        Add(PolicyDomains.CoreRecruitment, "R-OFF-ISSUE", "إصدار عرض → workflow", 10,
            """{"all":[{"field":"action","operator":"Equals","value":"Issue"}]}""",
            """[{"effect":"TriggerWorkflow","params":{"workflowCode":"RECRUITMENT-OFFER-APPROVAL"}}]"""),
        Add(PolicyDomains.CoreRecruitment, "R-COMMITTEE-SUBMIT", "تقديم لجنة اختيار → workflow", 10,
            """{"all":[{"field":"action","operator":"Equals","value":"SubmitCommittee"}]}""",
            """[{"effect":"TriggerWorkflow","params":{"workflowCode":"RECRUITMENT-SELECTION-COMMITTEE"}}]"""),
        Add(PolicyDomains.CoreRecruitment, "R-APPEAL-FILE", "تقديم استئناف → workflow", 10,
            """{"all":[{"field":"action","operator":"Equals","value":"FileAppeal"}]}""",
            """[{"effect":"TriggerWorkflow","params":{"workflowCode":"RECRUITMENT-APPEAL-REVIEW"}}]"""),
        Add(PolicyDomains.CoreRecruitment, "R-REC-HIRE-OFFER", "تعيين يتطلب عرضاً مقبولاً", 6,
            """{"all":[{"field":"action","operator":"Equals","value":"Hire"},{"field":"hasAcceptedOffer","operator":"Equals","value":"false"}]}""",
            """[{"effect":"Deny","params":{"reason":"يجب قبول خطاب العرض قبل التعيين"}}]"""),

        Field(PolicyDomains.ArchiveRetention, "legalHold", "bool", "حجز قانوني"),
        Add(PolicyDomains.ArchiveRetention, "R-ARC-HOLD", "حجز قانوني → منع الحذف", 5,
            """{"all":[{"field":"legalHold","operator":"Equals","value":"true"},{"field":"action","operator":"Equals","value":"Delete"}]}""",
            """[{"effect":"Deny","params":{"reason":"سجل محجوز قانونياً"}}]"""),

        Add(PolicyDomains.AssetsCustody, "R-CLASSIFY-HIGH-VALUE", "تصنيف — قيمة عالية → مميز", 5,
            """{"all":[{"field":"action","operator":"Equals","value":"Classify"},{"field":"value","operator":"GreaterThan","value":"10000"}]}""",
            """[{"effect":"SetField","params":{"field":"isPrivileged","value":"true"}}]"""),
        Add(PolicyDomains.AssetsCustody, "R-CLASSIFY-CAPITAL", "تصنيف — أصول ثابتة → مميز", 6,
            """{"all":[{"field":"action","operator":"Equals","value":"Classify"},{"field":"category","operator":"Equals","value":"أصول ثابتة"},{"field":"value","operator":"GreaterThan","value":"5000"}]}""",
            """[{"effect":"SetField","params":{"field":"isPrivileged","value":"true"}}]"""),
        Add(PolicyDomains.AssetsCustody, "R-PRIV-CODE", "مميز → قالب ترميز VIP", 12,
            """{"all":[{"field":"isPrivileged","operator":"Equals","value":"true"},{"field":"action","operator":"Equals","value":"Create"}]}""",
            """[{"effect":"SelectCodePolicy","params":{"policyKey":"ASSET-PRIVILEGED"}}]""")
    ]);

    /// <summary>
    /// PLAN-25 (G-14) retired the orphan RECRUITMENT-HIRE trigger by rewriting R-REC-HIRE in place. PLAN-47: a new version of
    /// Core.Recruitment without the rule, wherever the effective version still has it. Earlier versions keep it as they were.
    /// </summary>
    public static readonly PolicySeedChangeSet RetireRecruitmentHire = new(RetireRecruitmentHireId,
    [
        new(PolicySeedOperationKind.RetireRule, PolicyDomains.CoreRecruitment, RuleKey: "R-REC-HIRE",
            ReasonAr: "التعيين محكوم باعتماد خطاب العرض، ومسار RECRUITMENT-HIRE مسحوب (PLAN-25)")
    ]);

    /// <summary>
    /// PLAN-49 (G-POL-02, D-POL-1): the fifteen domains with no call site leave the engine. Wherever the national template exists
    /// it gets one final version with no rules and is marked retired; the earlier versions, and the ledger rows that name them,
    /// stay as they were. A new database seeds none of these templates, so the set passes over it. The reasons are this set's
    /// own text: an applied set never changes, whatever the catalog says later.
    /// </summary>
    public static readonly PolicySeedChangeSet ScopeRetire = new(ScopeRetireId,
    [
        RetireDomain(PolicyDomains.AssetsProcurement, "خارج نطاق المحرك (D-POL-1): لا نقطة استدعاء؛ استلام التوريد تحكمه قواعد Admin.Logistics المحلية"),
        RetireDomain(PolicyDomains.ContractsAdvance, "خارج نطاق المحرك (D-POL-1): لا نقطة استدعاء؛ قاعدة R-ADV-HIGH بأثر لا يستهلكه أحد (RequireDualApproval) وبقيمة نائبة"),
        RetireDomain(PolicyDomains.ContractsHire, "خارج نطاق المحرك (D-POL-1): لا نقطة استدعاء؛ قاعدة R-HIRE-START تطلق مساراً مسحوباً (HIRE-WIZARD)"),
        RetireDomain(PolicyDomains.ContractsSeparation, "خارج نطاق المحرك (D-POL-1): لا نقطة استدعاء؛ قاعدة R-SEP-START تطلق مساراً مسحوباً (SEPARATION-WIZARD)"),
        RetireDomain(PolicyDomains.ContractsContract, "خارج نطاق المحرك (D-POL-1): لا نقطة استدعاء؛ قاعدة R-CON-RENEW بأثر لا يستهلكه أحد (RequireApproval)"),
        RetireDomain(PolicyDomains.MovementsTransfer, "خارج نطاق المحرك (D-POL-1): لا نقطة استدعاء؛ قاعدة R-TRF تطلق مساراً مسحوباً (TRANSFER-APPROVAL)"),
        RetireDomain(PolicyDomains.MovementsWorkforce, "خارج نطاق المحرك (D-POL-1): لا نقطة استدعاء ولا قواعد"),
        RetireDomain(PolicyDomains.CoreEmployment, "خارج نطاق المحرك (D-POL-1): لا نقطة استدعاء ولا قواعد"),
        RetireDomain(PolicyDomains.HealthInsurance, "خارج نطاق المحرك (D-POL-1): لا نقطة استدعاء؛ قاعدة R-INS-MASK بأثر لا يستهلكه أحد (RequirePrivacyMask)"),
        RetireDomain(PolicyDomains.ComplianceDisciplinary, "خارج نطاق المحرك (D-POL-1): لا نقطة استدعاء؛ اعتماد الإجراء التأديبي يبدأ بالعقد العابر للخدمات (PLAN-46) لا بقاعدة R-DISC"),
        RetireDomain(PolicyDomains.PayrollPeriod, "خارج نطاق المحرك (D-POL-1): لا نقطة استدعاء؛ قاعدة R-PAY-CLOSE بأثر لا يستهلكه أحد (RequireDualApproval)"),
        RetireDomain(PolicyDomains.ITTickets, "خارج نطاق المحرك (D-POL-1): لا نقطة استدعاء؛ قاعدة R-TKT تطلق مساراً مسحوباً (IT-TICKET)"),
        RetireDomain(PolicyDomains.ArchiveRetention, "خارج نطاق المحرك (D-POL-1): لا نقطة استدعاء؛ الاحتفاظ والحجز القانوني تحكمهما قواعد Records.Archive المحلية"),
        RetireDomain(PolicyDomains.TrainingLearning, "خارج نطاق المحرك (D-POL-1): لا نقطة استدعاء؛ قاعدة R-TRN بأثر لا يستهلكه أحد (BlockUntilChecklist)"),
        RetireDomain(PolicyDomains.AnalyticsEtl, "خارج نطاق المحرك (D-POL-1): لا نقطة استدعاء؛ قاعدة R-ETL بأثر لا يستهلكه أحد (RequireApproval)")
    ]);

    /// <summary>
    /// PLAN-49 (G-POL-02): the three custody rules that had no condition on the action, as they are from now on. The handover
    /// (Assign, of type AssetAssignment) is the only call site that reads RequireEvidence and TriggerWorkflow, so the decision of
    /// a handover does not change; Classify and Create (type Asset) stop carrying effects nobody reads there. A new database is
    /// seeded with these same rules (<see cref="PolicyEngineSeeder"/>), so this set finds nothing to replace on it.
    /// </summary>
    public static readonly IReadOnlyList<PolicySeedRule> CustodyAssignRules =
    [
        new("R-PRIV-EVIDENCE", "أصل مميز → دليل", 10,
            """{"all":[{"field":"isPrivileged","operator":"Equals","value":"true"},{"field":"action","operator":"Equals","value":"Assign"}]}""",
            """[{"effect":"RequireEvidence"}]"""),
        new("R-PRIV-WF", "مميز → workflow", 15,
            """{"all":[{"field":"isPrivileged","operator":"Equals","value":"true"},{"field":"action","operator":"Equals","value":"Assign"}]}""",
            """[{"effect":"TriggerWorkflow","params":{"workflowCode":"ASSET-ASSIGN-APPROVAL"}}]"""),
        new("R-HIGH-APPROVAL", "قيمة عالية → موافقة", 20,
            """{"all":[{"field":"value","operator":"GreaterThan","value":"10000"},{"field":"action","operator":"Equals","value":"Assign"}]}""",
            """[{"effect":"TriggerWorkflow","params":{"workflowCode":"ASSET-ASSIGN-APPROVAL"}}]""")
    ];

    public static readonly PolicySeedChangeSet AssetsAction = new(AssetsActionId,
        CustodyAssignRules.Select(rule => Replace(PolicyDomains.AssetsCustody, rule)).ToList());

    /// <summary>
    /// PLAN-49 (G-POL-11): R-ID-PROCESS fired ID-CARD-ISSUANCE on every processed card, and nothing consumed the instance: the
    /// card's approval starts through the cross-service contract (PLAN-46). The rule goes; the definition and its binding are
    /// PLAN-46's and stay.
    /// </summary>
    public static readonly PolicySeedChangeSet IdCardOrphan = new(IdCardOrphanId,
    [
        Retire(PolicyDomains.IDCardLifecycle, "R-ID-PROCESS",
            "اعتماد البطاقة يبدأ بالعقد العابر للخدمات (PLAN-46)؛ القاعدة كانت تطلق مسار ID-CARD-ISSUANCE عند كل معالجة ولا يستهلكه أحد (G-POL-11)")
    ]);

    /// <summary>
    /// PLAN-49 (D-POL-5): no placeholder legal value and no effect without a consumer in the leave policy. R-LEAVE-MAX (30 days
    /// for every type) and R-LEAVE-LONG (RequireApproval) go; the local limits stay until the legal owner decides. R-LEAVE-SELF
    /// is bound to the approval, the only action that sends isSelfApproval.
    /// </summary>
    public static readonly PolicySeedRule LeaveSelfApprovalRule = new("R-LEAVE-SELF", "منع الموافقة الذاتية", 5,
        """{"all":[{"field":"action","operator":"Equals","value":"Approve"},{"field":"isSelfApproval","operator":"Equals","value":"true"}]}""",
        """[{"effect":"Deny","params":{"reason":"لا يمكن الموافقة على إجازتك"}}]""");

    public static readonly PolicySeedChangeSet Leave = new(LeaveId,
    [
        Retire(PolicyDomains.ContractsLeave, "R-LEAVE-MAX", "قيمة نائبة بلا مصدر قانوني: 30 يوماً لكل الأنواع (D-POL-5)؛ الحدود المحلية تبقى حتى قرار المالك القانوني"),
        Retire(PolicyDomains.ContractsLeave, "R-LEAVE-LONG", "أثر RequireApproval لا يستهلكه أحد (D-POL-5)"),
        Replace(PolicyDomains.ContractsLeave, LeaveSelfApprovalRule)
    ]);

    public static readonly IReadOnlyList<PolicySeedChangeSet> All =
        [Baseline, RetireRecruitmentHire, ScopeRetire, AssetsAction, IdCardOrphan, Leave];
}

/// <summary>PLAN-49: a seeded rule written once, for the seed of a new database and for the change set that brings an existing one to it.</summary>
public sealed record PolicySeedRule(string Key, string Name, int Priority, string When, string Then);
