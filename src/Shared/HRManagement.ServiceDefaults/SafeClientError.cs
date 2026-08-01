namespace HRManagement.ServiceDefaults;

/// <summary>Never surface raw exception messages to clients (SEC-001).</summary>
public static class SafeClientError
{
    public const string GenericArabic = "تعذر إتمام العملية. راجع سجل النظام أو تواصل مع الدعم.";
    public const string ValidationArabic = "البيانات المدخلة غير صالحة.";

    public static string ForUser(Exception ex, string? fallbackArabic = null) =>
        ex is ArgumentException or InvalidOperationException
            ? (string.IsNullOrWhiteSpace(ex.Message) ? GenericArabic : ex.Message)
            : fallbackArabic ?? GenericArabic;
}
