namespace HR.Core.Domain.Services;

/// <summary>SYR-007: validation messages and enqueue rules for service regime exceptions.</summary>
public static class ServiceRegimeExceptionRules
{
    public const string AlreadyResolvedMessage = "تم حل هذا الاستثناء مسبقاً.";
    public const string RegimeRequiredOnResolveMessage = "يجب تحديد نظام خدمة عند حل الاستثناء.";
    public const string ResolutionRequiredMessage = "يجب إدخال ملاحظة الحل.";
    public const string NotFoundMessage = "الاستثناء غير موجود.";

    public static bool ShouldEnqueueOnHire(string? legalRegimeId) =>
        string.IsNullOrWhiteSpace(legalRegimeId);

    public static string BuildHireReason(string? legalSourceId) =>
        string.IsNullOrWhiteSpace(legalSourceId)
            ? "تعيين بدون مرجع قانوني قابل للتعيين على نظام خدمة."
            : $"تعيين بمرجع قانوني ({legalSourceId}) لا يُطابق نظام خدمة معروف.";

    public static string BuildBackfillReason() =>
        "سجل وظيفي نشط بدون نظام خدمة — يتطلب تصنيفاً يدوياً.";

    public static string BuildTransitionReason() =>
        "انتقال/حركة أدت إلى سجل وظيفي نشط بدون نظام خدمة مصنّف.";

    public static string BuildArticle159Reason(string? fromRegimeId, string? toRegimeId) =>
        $"تعارض محتمل بموجب المادة 159 عند الانتقال من {fromRegimeId ?? "?"} إلى {toRegimeId ?? "?"}.";
}
