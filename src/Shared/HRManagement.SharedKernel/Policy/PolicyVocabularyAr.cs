namespace HRManagement.SharedKernel.Policy;

/// <summary>
/// PLAN-67 (§2, §5, D-67-7): the Arabic words of the policy pages and of <see cref="PolicyRuleReadableRenderer"/>. A key this file
/// does not know is shown as it is, as secondary text (an internal code), never inside the user's sentence.
/// </summary>
public static class PolicyVocabularyAr
{
    /// <summary>The eight operators of <see cref="PolicyRuleJson.Operators"/>.</summary>
    public static readonly IReadOnlyDictionary<string, string> Operators = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Equals"] = "يساوي",
        // The engine compares only a fact that is there: a missing fact matches no operator, so «لا يساوي» says the fact is set.
        ["NotEquals"] = "محدد ولا يساوي",
        ["GreaterThan"] = "أكبر من",
        ["LessThan"] = "أصغر من",
        ["GreaterThanOrEqual"] = "أكبر من أو يساوي",
        ["LessThanOrEqual"] = "أصغر من أو يساوي",
        ["Contains"] = "يحتوي",
        ["In"] = "ضمن",
    };

    /// <summary>The eleven effects of <see cref="PolicyEffect"/>, as the verb of the rule's sentence.</summary>
    public static readonly IReadOnlyDictionary<PolicyEffect, string> Effects = new Dictionary<PolicyEffect, string>
    {
        [PolicyEffect.Allow] = "يُسمح بالإجراء",
        [PolicyEffect.Deny] = "يُرفض الإجراء",
        [PolicyEffect.RequireEvidence] = "يتطلب إرفاق دليل",
        [PolicyEffect.RequireApproval] = "يتطلب موافقة",
        [PolicyEffect.RequireDualApproval] = "يتطلب موافقة مزدوجة (أربع عيون)",
        [PolicyEffect.TriggerWorkflow] = "يُطلق المسار",
        [PolicyEffect.SetField] = "يضبط الحقل",
        [PolicyEffect.RequireOriginReceipt] = "يتطلب إيصال الأصل",
        [PolicyEffect.RequirePrivacyMask] = "يتطلب إخفاء البيانات الشخصية",
        [PolicyEffect.BlockUntilChecklist] = "يُوقف الإجراء حتى تكتمل قائمة التحقق",
        [PolicyEffect.SelectCodePolicy] = "يختار سياسة الترميز",
    };

    /// <summary>The actions a call site sends (<see cref="PolicyDomainDescriptor.SubjectActions"/>), and the stored action names of the ledger.</summary>
    public static readonly IReadOnlyDictionary<string, string> Actions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Classify"] = "تصنيف",
        ["Create"] = "إنشاء",
        ["Assign"] = "تسليم",
        ["Process"] = "معالجة",
        ["Update"] = "تعديل",
        ["Approve"] = "اعتماد",
        ["Reject"] = "رفض",
        ["SubmitForApproval"] = "تقديم للاعتماد",
        ["Publish"] = "نشر",
        ["PublishInternal"] = "نشر داخلي",
        ["Hire"] = "تعيين",
        ["Issue"] = "إصدار",
        ["SubmitCommittee"] = "رفع إلى اللجنة",
        ["FileAppeal"] = "تقديم تظلّم",
        ["Return"] = "استرداد",
        ["ReportLoss"] = "إبلاغ عن فقدان",
        ["Delete"] = "حذف",
        ["Cancel"] = "إلغاء",
    };

    /// <summary>The workflow codes a rule may name (<see cref="PolicyDomainDescriptor.WorkflowTriggers"/>).</summary>
    public static readonly IReadOnlyDictionary<string, string> WorkflowCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ASSET-ASSIGN-APPROVAL"] = "اعتماد تسليم العهدة",
        ["RECRUITMENT-REQUISITION-APPROVAL"] = "اعتماد طلب الشاغر",
        ["RECRUITMENT-OFFER-APPROVAL"] = "اعتماد عرض التعيين",
        ["RECRUITMENT-OFFER-OVERRIDE-APPROVAL"] = "اعتماد تجاوز عرض التعيين",
        ["RECRUITMENT-SELECTION-COMMITTEE"] = "لجنة الاختيار",
        ["RECRUITMENT-APPEAL-REVIEW"] = "مراجعة التظلّم",
        ["ESTABLISHMENT-CHANGE-APPROVAL"] = "اعتماد تغيير الملاك",
    };

    /// <summary>The services that own a domain.</summary>
    public static readonly IReadOnlyDictionary<string, string> Services = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["HR.Core"] = "الموارد البشرية",
        ["Admin.Logistics"] = "الشؤون الإدارية واللوجستية",
        ["IDCard"] = "بطاقات التعريف",
        ["HR.Contracts"] = "العقود والإجازات",
        ["HR.Movements"] = "الحركة الوظيفية",
        ["HR.Health"] = "الصحة المهنية",
        ["HR.Training"] = "التدريب",
        ["Compliance"] = "الامتثال",
        ["Payroll"] = "الرواتب",
        ["IT.Infra"] = "البنية التقنية",
        ["Records.Archive"] = "الأرشيف",
        ["Dev.Analytics"] = "التحليلات",
    };

    /// <summary>A domain's Arabic name, from its descriptor (<see cref="PolicyDomainDescriptor.NameAr"/>); an unknown key as it is.</summary>
    public static string DomainName(string? domain) => PolicyDomainCatalog.Find(domain)?.NameAr ?? domain ?? "—";

    public static string ServiceName(string? service) => service is not null && Services.TryGetValue(service, out var ar) ? ar : service ?? "—";

    public static string Action(string? action) => action is not null && Actions.TryGetValue(action, out var ar) ? ar : action ?? "—";

    public static string SubjectType(string? subjectType) => HRManagement.SharedKernel.Localization.EntityTypeNamesAr.Get(subjectType);

    /// <summary>
    /// PLAN-67 (§5): a rule's name as the page shows it. Older seeds end a name with «→ workflow»; a published version is never
    /// rewritten, so the page reads that suffix in Arabic. A rule without a name shows its key.
    /// </summary>
    public static string RuleName(string? name, string? ruleKey) =>
        string.IsNullOrWhiteSpace(name)
            ? ruleKey ?? "—"
            : System.Text.RegularExpressions.Regex.Replace(name, @"\s*→\s*workflow\s*$", " — يُطلق مسار الاعتماد", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static string Workflow(string? code) => code is not null && WorkflowCodes.TryGetValue(code, out var ar) ? ar : code ?? "—";
}
