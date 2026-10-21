using HR.Core.Domain.Recruitment;

namespace HR.Core.Domain.Services;

/// <summary>HCM-026: validation of a versioned assessment criteria set.</summary>
public static class AssessmentCriteriaRules
{
    public const decimal RequiredWeightTotal = 100m;
    public const decimal WeightTolerance = 0.01m;
    public const int MaxKeyLength = 64;
    public const int MaxNameLength = 200;

    public const string RequiredBeforePublishMessage = "يجب اعتماد معايير التقييم قبل النشر.";

    public static string? Validate(IReadOnlyList<AssessmentCriteriaItem>? items)
    {
        if (items is null || items.Count == 0)
            return "يجب إضافة بند تقييم واحد على الأقل.";

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Key))
                return "مفتاح البند مطلوب.";
            if (item.Key.Length > MaxKeyLength)
                return $"مفتاح البند يجب ألا يتجاوز {MaxKeyLength} حرفاً.";
            if (!keys.Add(item.Key.Trim()))
                return $"مفتاح البند «{item.Key}» مكرر.";
            if (string.IsNullOrWhiteSpace(item.NameAr))
                return "اسم البند مطلوب.";
            if (item.NameAr.Length > MaxNameLength)
                return $"اسم البند يجب ألا يتجاوز {MaxNameLength} حرف.";
            if (item.Weight <= 0)
                return $"وزن البند «{item.NameAr}» يجب أن يكون أكبر من صفر.";
            if (item.MaxScore <= 0)
                return $"الحد الأعلى لدرجة البند «{item.NameAr}» يجب أن يكون أكبر من صفر.";
        }

        var total = items.Sum(i => i.Weight);
        if (Math.Abs(total - RequiredWeightTotal) > WeightTolerance)
            return $"مجموع الأوزان يجب أن يساوي 100 (المجموع الحالي {total:0.##}).";

        return null;
    }
}
