using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace HRManagement.SharedKernel.Localization;

/// <summary>
/// PLAN-67 (§2, G-UX-03): the Arabic name of a record type stored as text (an approval's or a workflow's <c>EntityType</c>, a policy
/// template's subject). The class's own <c>[Display(Name = …)]</c> wins; otherwise this list; otherwise the stored name, unchanged.
/// </summary>
public static class EntityTypeNamesAr
{
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["AdvanceRequest"] = "طلب سلفة",
        ["ApprovalDelegation"] = "تفويض اعتماد",
        ["ApprovalRequest"] = "طلب اعتماد",
        ["ApprovedCV"] = "سيرة ذاتية معتمدة",
        ["ArchiveItem"] = "مادة أرشيف",
        ["Asset"] = "أصل",
        ["AssetAssignment"] = "تسليم عهدة",
        ["AssetLossReport"] = "بلاغ فقدان أصل",
        ["CardRequest"] = "طلب بطاقة",
        ["Contract"] = "عقد",
        ["DisciplinaryAction"] = "إجراء انضباطي",
        ["Employee"] = "موظف",
        ["EosSettlement"] = "تسوية نهاية الخدمة",
        ["EstablishmentChangeRequest"] = "طلب تغيير ملاك",
        ["FundedPosition"] = "منصب ممول",
        ["JobCatalogEntry"] = "وظيفة في الدليل",
        ["LeaveRequest"] = "طلب إجازة",
        ["LegalDataChange"] = "تعديل بيانات قانونية",
        ["LoanAssignmentRequest"] = "طلب إعارة",
        ["OfferLetter"] = "عرض تعيين",
        ["PaymentBatch"] = "دفعة صرف",
        ["PayrollRun"] = "مسير رواتب",
        ["PromotionRequest"] = "طلب ترفيع",
        ["RecruitmentApplication"] = "طلب توظيف",
        ["RecruitmentAppeal"] = "تظلّم توظيف",
        ["RecruitmentAssessmentCriteria"] = "معايير التقييم",
        ["RecruitmentCompetition"] = "مسابقة توظيف",
        ["RecruitmentRequisition"] = "طلب شاغر",
        ["RoleAssignment"] = "إسناد دور",
        ["SecondmentRequest"] = "طلب ندب",
        ["SelectionCommitteeSession"] = "جلسة لجنة اختيار",
        ["SeparationRecord"] = "إنهاء خدمة",
        ["ServiceTransaction"] = "معاملة خدمة",
        ["SupplyOrder"] = "أمر توريد",
        ["TransferRequest"] = "طلب نقل",
        ["WorkforcePlan"] = "خطة قوى عاملة",
    };

    private static readonly ConcurrentDictionary<string, string?> FromDisplay = new(StringComparer.Ordinal);

    public static string Get(string? entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType))
            return "—";
        var name = entityType.Trim();
        var display = FromDisplay.GetOrAdd(name, static n => DisplayOfClass(n));
        if (display is not null)
            return display;
        return Names.TryGetValue(name, out var ar) ? ar : name;
    }

    private static string? DisplayOfClass(string shortName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic
                     && a.FullName?.StartsWith("System.", StringComparison.Ordinal) != true
                     && a.FullName?.StartsWith("Microsoft.", StringComparison.Ordinal) != true))
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).ToArray()!;
            }

            var match = types.FirstOrDefault(t => t.IsClass && string.Equals(t.Name, shortName, StringComparison.Ordinal));
            if (match?.GetCustomAttribute<DisplayAttribute>()?.Name is { Length: > 0 } name)
                return name;
        }

        return null;
    }
}
