using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults;

public static class HrManagementAuthorizationExtensions
{
    /// <summary>Registers module and fine-grained authorization policies.</summary>
    public static IServiceCollection AddHrManagementAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(HrManagementPolicies.ModuleHrCore, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.HrCore)
                || c.User.IsInRole(HrManagementRoles.HrCoreSupervisory)
                || c.User.IsInRole(HrManagementRoles.HrCoreExecutive)
                || c.User.IsInRole(HrManagementRoles.BranchManager)));
            options.AddPolicy(HrManagementPolicies.ModuleEss, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.HrEmployee)));
            AddModulePolicy(options, HrManagementPolicies.ModuleHrContracts, HrManagementRoles.HrContracts);
            options.AddPolicy(HrManagementPolicies.ModuleHrHealth, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.HrHealth)
                || c.User.IsInRole(HrManagementRoles.HrHealthClinical)));
            options.AddPolicy(HrManagementPolicies.HrHealthClinical, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.HrHealthClinical)));
            AddModulePolicy(options, HrManagementPolicies.ModuleHrMovements, HrManagementRoles.HrMovements);
            AddModulePolicy(options, HrManagementPolicies.ModuleHrTraining, HrManagementRoles.HrTraining);
            AddModulePolicy(options, HrManagementPolicies.ModulePayroll, HrManagementRoles.Payroll);
            AddModulePolicy(options, HrManagementPolicies.ModuleIdCard, HrManagementRoles.IdCard);
            AddModulePolicy(options, HrManagementPolicies.ModuleRecordsArchive, HrManagementRoles.RecordsArchive);
            AddModulePolicy(options, HrManagementPolicies.ModuleAdminLogistics, HrManagementRoles.AdminLogistics);
            AddModulePolicy(options, HrManagementPolicies.ModuleItInfra, HrManagementRoles.ItInfra);
            AddModulePolicy(options, HrManagementPolicies.ModuleDevAnalytics, HrManagementRoles.DevAnalytics);
            AddModulePolicy(options, HrManagementPolicies.ModuleCompliance, HrManagementRoles.Compliance);
            AddModulePolicy(options, HrManagementPolicies.ModuleAuthDirectory, HrManagementRoles.AuthDirectory);
            options.AddPolicy(HrManagementPolicies.ModuleFileStorage, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.FileStorage)));

            // Gateway home: any signed-in user. Module paths still require module roles (middleware + downstream).
            options.AddPolicy(HrManagementPolicies.GatewayDashboard, p => p.RequireAuthenticatedUser());

            // HR.Core fine-grained policies
            options.AddPolicy(HrManagementPolicies.HrCoreReadOnly, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.HrCore)
                || c.User.IsInRole(HrManagementRoles.HrCoreSupervisory)
                || c.User.IsInRole(HrManagementRoles.HrCoreExecutive)
                || c.User.IsInRole(HrManagementRoles.BranchManager)));

            options.AddPolicy(HrManagementPolicies.HrCoreWrite, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.HrCore)));

            options.AddPolicy(HrManagementPolicies.HrCoreDelete, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)));

            options.AddPolicy(HrManagementPolicies.HrCoreApprove, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.HrCore)));

            // Shared lookup — interactive users OR service tokens with hr-core.lookup (IAM-004).
            options.AddPolicy(HrManagementPolicies.HrCoreLookup, p => p.RequireAssertion(c =>
                (c.User.Identity?.IsAuthenticated == true && !ServiceCallerPrincipal.IsServiceAccount(c.User))
                || (ServiceCallerPrincipal.IsServiceAccount(c.User)
                    && ServiceCallerPrincipal.HasScope(c.User, ServiceScopes.HrCoreLookup))));

            // HR.Contracts fine-grained: approve leave/advance/separation
            options.AddPolicy(HrManagementPolicies.HrContractsApprove, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.HrContracts)));

            // Payroll fine-grained: retain the legacy Payroll role temporarily while adding role separation.
            options.AddPolicy(HrManagementPolicies.PayrollProcess, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.Payroll)
                || c.User.IsInRole(HrManagementRoles.PayrollProcessor)));

            options.AddPolicy(HrManagementPolicies.PayrollApprove, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.Payroll)
                || c.User.IsInRole(HrManagementRoles.PayrollApprover)));

            options.AddPolicy(HrManagementPolicies.PayrollPost, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.Payroll)
                || c.User.IsInRole(HrManagementRoles.PayrollPoster)));

            options.AddPolicy(HrManagementPolicies.PayrollReverse, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.PayrollPoster)
                || c.User.IsInRole(HrManagementRoles.PayrollReverser)));

            options.AddPolicy(HrManagementPolicies.HrManagementApprove, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.HrManagementApprover)));

            options.AddPolicy(HrManagementPolicies.HrContractsHireExecute, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.HrContracts)
                || c.User.IsInRole(HrManagementRoles.HrCore)));

            options.AddPolicy(HrManagementPolicies.ManageDelegations, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.HrCore)
                || c.User.IsInRole(HrManagementRoles.HrManagementApprover)
                || c.User.IsInRole(HrManagementRoles.PlatformAdmin)));

            options.AddPolicy(HrManagementPolicies.FileStorageForceDelete, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.PlatformAdmin)
                || c.User.IsInRole(HrManagementRoles.FileStorage)));

            // EVD-003: read-only for admin / platform / compliance (no write/delete for app admin).
            options.AddPolicy(HrManagementPolicies.EvidenceRead, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.PlatformAdmin)
                || c.User.IsInRole(HrManagementRoles.Compliance)));
        });

        services.AddServiceToServiceAuthorizationPolicies();
        services.AddHrMvcSecurityDefaults();
        return services;
    }

    private static void AddModulePolicy(AuthorizationOptions options, string policyName, string moduleRole)
    {
        options.AddPolicy(
            policyName,
            p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin) || c.User.IsInRole(moduleRole)));
    }

    /// <summary>Requires the given module policy on all MVC actions (use after AddControllersWithViews).</summary>
    public static IMvcBuilder AddHrModuleAuthorizeFilter(this IMvcBuilder mvc, string policyName)
    {
        mvc.Services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(o =>
            o.Filters.Add(new HrModuleAuthorizationFilter(policyName)));
        return mvc;
    }
}
