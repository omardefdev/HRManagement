namespace HR.Core.Domain.Services;

/// <summary>SYR-001 / HCM: validation messages for recruitment requisition legal source binding.</summary>
public static class RecruitmentLegalSourceRules
{
    public const string RequiredOnSubmitMessage =
        "المرجع القانوني مطلوب قبل تقديم الطلب للاعتماد.";

    public const string NotFoundMessage =
        "المرجع القانوني غير موجود في سجل SYR-001.";

    public const string NotActiveMessage =
        "المرجع القانوني غير نشط في سجل SYR-001.";

    public const string RequiredOnPublishMessage =
        "لا يمكن النشر بدون مرجع قانوني.";

    public const string RequisitionMissingLegalSourceForOfferMessage =
        "لا يمكن إنشاء خطاب عرض بدون مرجع قانوني على طلب الشاغر.";

    public const string RequiredOnOfferSubmitMessage =
        "المرجع القانوني مطلوب قبل تقديم خطاب العرض للاعتماد.";

    public const string RequiredOnHireMessage =
        "لا يمكن التعيين بدون مرجع قانوني على خطاب العرض.";

    public const string LineageMismatchMessage =
        "مرجع خطاب العرض لا يطابق مرجع طلب الشاغر.";

    public static bool IsEmpty(string? legalSourceId) =>
        string.IsNullOrWhiteSpace(legalSourceId);

    public static bool LineageMatches(string? requisitionLegalSourceId, string? offerLegalSourceId) =>
        IsEmpty(requisitionLegalSourceId)
        || string.Equals(requisitionLegalSourceId, offerLegalSourceId, StringComparison.Ordinal);
}
