using HRManagement.Gateway.Models;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.ModuleCatalog;

namespace HRManagement.Gateway.Services;

public interface IGatewayModuleCatalog
{
    IReadOnlyList<GatewayModule> Modules { get; }
    GatewayModule? FindBySlug(string slug);
}

public sealed class GatewayModuleCatalog : IGatewayModuleCatalog
{
    private readonly IReadOnlyList<GatewayModule> _all;
    private readonly IModuleEntitlementGuard? _entitlementGuard;

    public GatewayModuleCatalog(
        IConfiguration configuration,
        IModuleEntitlementGuard? entitlementGuard = null)
    {
        _entitlementGuard = entitlementGuard;
        _all = BuildModules(configuration);
    }

    /// <summary>Modules after global flags and (when guard present) request-time agency pack overlay.</summary>
    public IReadOnlyList<GatewayModule> Modules =>
        _entitlementGuard is null
            ? _all
            : _all.Where(x => _entitlementGuard.IsModuleExposed(x.Slug)).ToList();

    private static IReadOnlyList<GatewayModule> BuildModules(IConfiguration configuration)
    {
        var all = new List<GatewayModule>
        {
            new("hr-core", "HR.Core", "نواة الموارد البشرية", "/ui/hr-core", 5010, "/health/upstreams/hr-core", HrManagementPolicies.ModuleHrCore, HrManagementRoles.HrCore),
            new("hr-contracts", "HR.Contracts", "العقود والخدمة", "/ui/hr-contracts", 5020, "/health/upstreams/hr-contracts", HrManagementPolicies.ModuleHrContracts, HrManagementRoles.HrContracts),
            new("hr-health", "HR.Health", "الصحة واللجان الطبية", "/ui/hr-health", 5030, "/health/upstreams/hr-health", HrManagementPolicies.ModuleHrHealth, HrManagementRoles.HrHealth),
            new("hr-movements", "HR.Movements", "التنقلات والتوزيع", "/ui/hr-movements", 5040, "/health/upstreams/hr-movements", HrManagementPolicies.ModuleHrMovements, HrManagementRoles.HrMovements),
            new("hr-training", "HR.Training", "التدريب والتأهيل", "/ui/hr-training", 5050, "/health/upstreams/hr-training", HrManagementPolicies.ModuleHrTraining, HrManagementRoles.HrTraining),
            new("payroll", "Payroll", "الرواتب والتعويضات", "/ui/payroll", 5060, "/health/upstreams/payroll", HrManagementPolicies.ModulePayroll, HrManagementRoles.Payroll),
            new("idcard", "IDCard", "الهويات والبطاقات", "/ui/idcard", 5070, "/health/upstreams/idcard", HrManagementPolicies.ModuleIdCard, HrManagementRoles.IdCard),
            new("records-archive", "Records.Archive", "الأرشيف والملفات", "/ui/records-archive", 5080, "/health/upstreams/records-archive", HrManagementPolicies.ModuleRecordsArchive, HrManagementRoles.RecordsArchive),
            new("admin-logistics", "Admin.Logistics", "الإدارة واللوجستيات", "/ui/admin-logistics", 5090, "/health/upstreams/admin-logistics", HrManagementPolicies.ModuleAdminLogistics, HrManagementRoles.AdminLogistics),
            new("it-infra", "IT.Infra", "تقنية المعلومات", "/ui/it-infra", 5100, "/health/upstreams/it-infra", HrManagementPolicies.ModuleItInfra, HrManagementRoles.ItInfra),
            new("dev-analytics", "Dev.Analytics", "التطوير والتحليل", "/ui/dev-analytics", 5110, "/health/upstreams/dev-analytics", HrManagementPolicies.ModuleDevAnalytics, HrManagementRoles.DevAnalytics),
            new("compliance", "Compliance", "الامتثال والرقابة", "/ui/compliance", 5120, "/health/upstreams/compliance", HrManagementPolicies.ModuleCompliance, HrManagementRoles.Compliance),
            new("auth-directory", "Auth.Directory", "المصادقة والدليل", "/ui/auth-directory", 5130, "/health/upstreams/auth-directory", HrManagementPolicies.ModuleAuthDirectory, HrManagementRoles.AuthDirectory),
            new("file-storage", "File.Storage", "إدارة الملفات", "/ui/file-storage", 5140, "/health/upstreams/file-storage", HrManagementPolicies.ModuleFileStorage, HrManagementRoles.FileStorage),
        };

        var enabled = configuration.GetSection("Gateway:EnabledModules").Get<string[]>() ?? [];
        if (enabled.Length == 0)
            return all;
        var set = new HashSet<string>(enabled, StringComparer.OrdinalIgnoreCase);
        return all.Where(x => set.Contains(x.Slug)).ToList();
    }

    public GatewayModule? FindBySlug(string slug) =>
        Modules.FirstOrDefault(x => string.Equals(x.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
