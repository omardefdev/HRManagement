namespace HR.Contracts.Domain.Services;

/// <summary>FUN-029: Arabic messages of the hire contract and checklist rules.</summary>
public static class HireContractMessages
{
    public const string Unspecified = "غير محدد";

    /// <summary>Marker shared by the checklist note and the intervention reason.</summary>
    public const string UnresolvedMarker = "يتطلب تدخلاً";

    public static string UnresolvedContractType(string? legalRegimeId, string? employmentType) =>
        $"لا يوجد نوع عقد مطابق لنظام الخدمة {Value(legalRegimeId)} / {Value(employmentType)} — {UnresolvedMarker}";

    public const string CommencementOverdue = "تأخر المباشرة عن المهلة";

    private static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? Unspecified : value.Trim();
}
