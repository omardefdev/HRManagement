namespace HRManagement.Web.Shared.Models;

public sealed record HrModuleLink(
    string Slug,
    string TitleAr,
    string IconClass,
    string UiPath);

public static class HrModuleCatalog
{
    public static IReadOnlyList<HrModuleLink> ServiceModules { get; } =
    [
        new("hr-contracts", "العقود والخدمة", "fas fa-file-contract", "/ui/hr-contracts"),
        new("hr-health", "الصحة واللجان الطبية", "fas fa-heartbeat", "/ui/hr-health"),
        new("hr-movements", "التنقلات والتوزيع", "fas fa-exchange-alt", "/ui/hr-movements"),
        new("hr-training", "التدريب والتأهيل", "fas fa-graduation-cap", "/ui/hr-training"),
        new("payroll", "الرواتب والتعويضات", "fas fa-money-bill-wave", "/ui/payroll"),
        new("idcard", "الهويات والبطاقات", "fas fa-id-card", "/ui/idcard"),
        new("records-archive", "الأرشيف والملفات", "fas fa-archive", "/ui/records-archive"),
    ];

    public static IReadOnlyList<HrModuleLink> SupportModules { get; } =
    [
        new("admin-logistics", "الإدارة واللوجستيات", "fas fa-cogs", "/ui/admin-logistics"),
        new("it-infra", "تقنية المعلومات", "fas fa-server", "/ui/it-infra"),
        new("dev-analytics", "التطوير والتحليل", "fas fa-chart-bar", "/ui/dev-analytics"),
        new("compliance", "الامتثال والرقابة", "fas fa-balance-scale", "/ui/compliance"),
        new("auth-directory", "المصادقة والدليل", "fas fa-shield-alt", "/ui/auth-directory"),
    ];

    public static IReadOnlyList<HrModuleLink> AllModules =>
        ServiceModules.Concat(SupportModules).ToList();
}
