namespace HR.Core.Domain.Services;

/// <summary>SYR-003: validation messages for service regime assignment and benefit plans.</summary>
public static class ServiceRegimeRules
{
    public const string NotFoundMessage = "نظام الخدمة غير موجود.";
    public const string NotActiveMessage = "نظام الخدمة غير نشط أو خارج فترة السريان.";
    public const string MissingLegislationLinkMessage = "نظام الخدمة غير مرتبط بمرجع تشريعي في السجل الوطني.";
    public const string BenefitPlanUnknownLegalSourceMessage = "مرجع قانوني غير معروف في سجل التشريعات.";

    public static bool RequiresResolvedLegislation(string legalRegimeId) =>
        legalRegimeId is "law50" or "law53";
}
