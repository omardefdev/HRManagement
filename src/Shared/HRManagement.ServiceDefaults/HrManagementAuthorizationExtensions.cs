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
                || c.User.IsInRole(HrManagementRoles.BranchManager)
                || c.User.IsInRole(HrManagementRoles.HrManagementApprover)
                || (ServiceCallerPrincipal.IsServiceAccount(c.User)
                    && (ServiceCallerPrincipal.HasScope(c.User, ServiceScopes.HrCoreLookup)
                        || ServiceCallerPrincipal.HasScope(c.User, ServiceScopes.HrCoreWorkflow)))));
            options.AddPolicy(HrManagementPolicies.ModuleEss, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.HrEmployee)));
            // The management checker must be able to open the contracts module to review
            // pending hire cases; write and execution actions remain separately guarded.
            options.AddPolicy(HrManagementPolicies.ModuleHrContracts, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.HrContracts)
                || c.User.IsInRole(HrManagementRoles.HrManagementApprover)));
            options.AddPolicy(HrManagementPolicies.ModuleHrHealth, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.HrHealth)
                || c.User.IsInRole(HrManagementRoles.HrHealthClinical)));
            // PLAN-51 (D-IAM-4): roles from configuration, so Authorization:AdminExcludedPolicies can drop hr-admin outside demo mode.
            options.RolesPolicy(HrManagementPolicies.HrHealthClinical, HrManagementRoles.Admin, HrManagementRoles.HrHealthClinical);
            AddModulePolicy(options, HrManagementPolicies.ModuleHrMovements, HrManagementRoles.HrMovements);
            AddModulePolicy(options, HrManagementPolicies.ModuleHrTraining, HrManagementRoles.HrTraining);
            AddModulePolicy(options, HrManagementPolicies.ModulePayroll, HrManagementRoles.Payroll);
            AddModulePolicy(options, HrManagementPolicies.ModuleIdCard, HrManagementRoles.IdCard);
            AddModulePolicy(options, HrManagementPolicies.ModuleRecordsArchive, HrManagementRoles.RecordsArchive);
            AddModulePolicy(options, HrManagementPolicies.ModuleAdminLogistics, HrManagementRoles.AdminLogistics);
            AddModulePolicy(options, HrManagementPolicies.ModuleItInfra, HrManagementRoles.ItInfra);
            options.AddPolicy(HrManagementPolicies.ModuleDevAnalytics, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.DevAnalytics)
                || c.User.IsInRole(HrManagementRoles.PlatformAdmin)));
            AddModulePolicy(options, HrManagementPolicies.ModuleCompliance, HrManagementRoles.Compliance);
            AddModulePolicy(options, HrManagementPolicies.ModuleAuthDirectory, HrManagementRoles.AuthDirectory);
            options.AddPolicy(HrManagementPolicies.ModuleFileStorage, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.FileStorage)
                || (ServiceCallerPrincipal.IsServiceAccount(c.User)
                    && ServiceCallerPrincipal.HasScope(c.User, ServiceScopes.FileStorageReferences))));

            // PLAN-51 (review M1): File.Storage's own writes (upload, delete) name a write policy: the module's roles, unchanged.
            options.AddPolicy(HrManagementPolicies.FileStorageWrite, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.FileStorage)
                || (ServiceCallerPrincipal.IsServiceAccount(c.User)
                    && ServiceCallerPrincipal.HasScope(c.User, ServiceScopes.FileStorageReferences))));

            // Gateway home: any signed-in user. Module paths still require module roles (middleware + downstream).
            options.AddPolicy(HrManagementPolicies.GatewayDashboard, p => p.RequireAuthenticatedUser());

            // HR.Core fine-grained policies
            options.AddPolicy(HrManagementPolicies.HrCoreReadOnly, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.HrCore)
                || c.User.IsInRole(HrManagementRoles.HrCoreSupervisory)
                || c.User.IsInRole(HrManagementRoles.HrCoreExecutive)
                || c.User.IsInRole(HrManagementRoles.BranchManager)
                || c.User.IsInRole(HrManagementRoles.HrManagementApprover)
                || (ServiceCallerPrincipal.IsServiceAccount(c.User)
                    && (ServiceCallerPrincipal.HasScope(c.User, ServiceScopes.HrCoreLookup)
                        || ServiceCallerPrincipal.HasScope(c.User, ServiceScopes.HrCoreWorkflow)))));

            // PLAN-51 (§5, D-51-4): no service branch. hr-core.workflow is accepted on three endpoints only (Workflow.ExternalStart,
            // Service.CodeGenerate, Service.PolicyEnforce); a service account writes nothing in HR.Core's records. Roles from configuration
            // (D-51-2), by default hr-admin and hr-core.
            options.RolesPolicy(HrManagementPolicies.HrCoreWrite, HrManagementRoles.Admin, HrManagementRoles.HrCore);

            // PLAN-47 (D-POL-3): the policy lifecycle; the role lists are read from configuration by PolicyEngineRoleHandler.
            options.AddPolicy(HrManagementPolicies.PolicyAuthor, p => p.AddRequirements(new PolicyEngineRoleRequirement(PolicyEngineRoleKind.Author)));
            options.AddPolicy(HrManagementPolicies.PolicyApprover, p => p.AddRequirements(new PolicyEngineRoleRequirement(PolicyEngineRoleKind.Approver)));
            options.AddPolicy(HrManagementPolicies.PolicyReader, p => p.AddRequirements(new PolicyEngineRoleRequirement(PolicyEngineRoleKind.Reader)));

            options.AddPolicy(HrManagementPolicies.WorkflowDefinitionAdmin, p => p.RequireAssertion(c =>
                !ServiceCallerPrincipal.IsServiceAccount(c.User)
                && (c.User.IsInRole(HrManagementRoles.Admin) || c.User.IsInRole(HrManagementRoles.WorkflowAdmin))));

            options.AddPolicy(HrManagementPolicies.WorkflowExternalStart, p => p.RequireAssertion(c =>
                ServiceCallerPrincipal.IsServiceAccount(c.User)
                && ServiceCallerPrincipal.HasScope(c.User, ServiceScopes.HrCoreWorkflow)));

            // PLAN-50 (§6, P-6): the roles, or a delegate of any delegable role (the evaluator then decides on the step, the type and the
            // principal); still never a service account.
            options.AddPolicy(HrManagementPolicies.WorkflowStepAction, p => p
                .RequireAssertion(c => !ServiceCallerPrincipal.IsServiceAccount(c.User))
                .AddRequirements(new Delegation.RoleOrDelegationRequirement(
                [
                    HrManagementRoles.Admin,
                    HrManagementRoles.HrCore,
                    HrManagementRoles.BranchManager,
                    HrManagementRoles.HrManagementApprover,
                    // PLAN-45, PLAN-46: the steps of custody, loss, the card and discipline. These roles reach no other page of HR.Core than the
                    // inbox and the approval page: they stay out of ModuleHrCore and HrCoreReadOnly.
                    HrManagementRoles.AdminLogistics,
                    HrManagementRoles.IdCard,
                    HrManagementRoles.Compliance
                ], delegatedRole: null, anyDelegableRole: true)));

            options.AddPolicy(HrManagementPolicies.WorkflowDefinitionRead, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.HrCore)
                || c.User.IsInRole(HrManagementRoles.HrCoreSupervisory)
                || c.User.IsInRole(HrManagementRoles.HrCoreExecutive)
                || c.User.IsInRole(HrManagementRoles.BranchManager)
                || c.User.IsInRole(HrManagementRoles.HrManagementApprover)
                || c.User.IsInRole(HrManagementRoles.WorkflowAdmin)
                || (ServiceCallerPrincipal.IsServiceAccount(c.User)
                    && (ServiceCallerPrincipal.HasScope(c.User, ServiceScopes.HrCoreLookup)
                        || ServiceCallerPrincipal.HasScope(c.User, ServiceScopes.HrCoreWorkflow)))));

            options.AddPolicy(HrManagementPolicies.MessagingRepair, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.PlatformAdmin)));

            options.AddPolicy(HrManagementPolicies.HrCoreDelete, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)));

            options.AddPolicy(HrManagementPolicies.HrCoreApprove, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.HrCore)
                || c.User.IsInRole(HrManagementRoles.HrManagementApprover)
                || c.User.IsInRole(HrManagementRoles.PlatformAdmin)));

            options.AddPolicy(HrManagementPolicies.TenantQuarantineReconcile, p => p.RequireRole(
                HrManagementRoles.PlatformAdmin));

            // PLAN-51: HrCore.BranchOperation is gone; PLAN-50 moved its last actions to Workflow.StepAction.

            // HCM-028 (PLAN-25 slice): the compliance reviewer reaches HR.Core only for recruitment appeals.
            options.AddPolicy(HrManagementPolicies.HrCoreAppealReview, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.HrCore)
                || c.User.IsInRole(HrManagementRoles.Compliance)));

            options.AddPolicy(HrManagementPolicies.HrCoreWorkInbox, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.HrCore)
                || c.User.IsInRole(HrManagementRoles.HrCoreSupervisory)
                || c.User.IsInRole(HrManagementRoles.HrCoreExecutive)
                || c.User.IsInRole(HrManagementRoles.BranchManager)
                || c.User.IsInRole(HrManagementRoles.HrManagementApprover)
                || c.User.IsInRole(HrManagementRoles.Compliance)
                || c.User.IsInRole(HrManagementRoles.AdminLogistics)
                || c.User.IsInRole(HrManagementRoles.IdCard)));

            // HCM-031 (PLAN-31): exam markers reach HR.Core only for the anonymous marking screens. Not added to
            // ModuleHrCore / HrCoreReadOnly / HrCoreWorkInbox, so a marker never reaches application data.
            options.AddPolicy(HrManagementPolicies.HrCoreExamMarking, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.HrExamMarker)));

            // Shared lookup (IAM-004). PLAN-51 (D-51-3): interactive staff (PLAN-50's one definition) or service tokens with hr-core.lookup;
            // the self-service employee and the exam marker do not search the agency's directory.
            options.AddPolicy(HrManagementPolicies.HrCoreLookup, p => p.AddRequirements(new InteractiveStaffOrServiceScopeRequirement(ServiceScopes.HrCoreLookup)));

            // PLAN-51 (§2, P-4): files that carry no data.
            options.AddPolicy(HrManagementPolicies.GatewayAuthenticated, p => p.RequireAuthenticatedUser());

            // PLAN-51 (§3, D-IAM-7): the gateway's audit log is for its owners; the rows are filtered by agency in the query service.
            options.RolesPolicyAt(HrManagementPolicies.GatewayAudit, "Gateway:Audit:Roles",
                HrManagementRoles.PlatformAdmin, HrManagementRoles.Auditor, HrManagementRoles.Compliance);

            // HR.Contracts fine-grained: approve leave/advance/separation. PLAN-51 (D-51-2): roles from configuration, today's by default.
            options.RolesPolicy(HrManagementPolicies.HrContractsApprove, HrManagementRoles.Admin, HrManagementRoles.HrContracts);

            // PLAN-51 (§4, D-51-2): the write and approval policy of each module. Defaults = today's roles of the module (HR.Contracts:
            // its writer, not the management approver who reads it).
            options.RolesPolicy(HrManagementPolicies.HrCoreDecision, HrManagementRoles.Admin, HrManagementRoles.HrCore);
            options.RolesPolicy(HrManagementPolicies.HrContractsWrite, HrManagementRoles.Admin, HrManagementRoles.HrContracts);
            options.RolesPolicy(HrManagementPolicies.PayrollWrite, HrManagementRoles.Admin, HrManagementRoles.Payroll);
            options.RolesPolicy(HrManagementPolicies.HrMovementsApprove, HrManagementRoles.Admin, HrManagementRoles.HrMovements);
            options.RolesPolicy(HrManagementPolicies.HrTrainingWrite, HrManagementRoles.Admin, HrManagementRoles.HrTraining);
            options.RolesPolicy(HrManagementPolicies.HrTrainingApprove, HrManagementRoles.Admin, HrManagementRoles.HrTraining);
            options.RolesPolicy(HrManagementPolicies.HrHealthWrite, HrManagementRoles.Admin, HrManagementRoles.HrHealth, HrManagementRoles.HrHealthClinical);
            options.RolesPolicy(HrManagementPolicies.HrHealthApprove, HrManagementRoles.Admin, HrManagementRoles.HrHealth, HrManagementRoles.HrHealthClinical);
            options.RolesPolicy(HrManagementPolicies.ComplianceWrite, HrManagementRoles.Admin, HrManagementRoles.Compliance);
            options.RolesPolicy(HrManagementPolicies.ComplianceApprove, HrManagementRoles.Admin, HrManagementRoles.Compliance);
            options.RolesPolicy(HrManagementPolicies.IdCardWrite, HrManagementRoles.Admin, HrManagementRoles.IdCard);
            options.RolesPolicy(HrManagementPolicies.IdCardApprove, HrManagementRoles.Admin, HrManagementRoles.IdCard);
            options.RolesPolicy(HrManagementPolicies.AdminLogisticsWrite, HrManagementRoles.Admin, HrManagementRoles.AdminLogistics);
            options.RolesPolicy(HrManagementPolicies.AdminLogisticsApprove, HrManagementRoles.Admin, HrManagementRoles.AdminLogistics);
            options.RolesPolicy(HrManagementPolicies.ItInfraWrite, HrManagementRoles.Admin, HrManagementRoles.ItInfra);
            options.RolesPolicy(HrManagementPolicies.ItInfraApprove, HrManagementRoles.Admin, HrManagementRoles.ItInfra);
            options.RolesPolicy(HrManagementPolicies.RecordsArchiveWrite, HrManagementRoles.Admin, HrManagementRoles.RecordsArchive);
            options.RolesPolicy(HrManagementPolicies.RecordsArchiveApprove, HrManagementRoles.Admin, HrManagementRoles.RecordsArchive);
            options.RolesPolicy(HrManagementPolicies.DevAnalyticsWrite, HrManagementRoles.Admin, HrManagementRoles.DevAnalytics, HrManagementRoles.PlatformAdmin);
            options.RolesPolicy(HrManagementPolicies.DevAnalyticsApprove, HrManagementRoles.Admin, HrManagementRoles.DevAnalytics, HrManagementRoles.PlatformAdmin);
            options.RolesPolicy(HrManagementPolicies.AuthDirectoryWrite, HrManagementRoles.Admin, HrManagementRoles.AuthDirectory);
            options.RolesPolicy(HrManagementPolicies.AuthDirectoryApprove, HrManagementRoles.Admin, HrManagementRoles.AuthDirectory);

            // PLAN-51 (decision 4): the roles-only writes, named with the same roles.
            options.RolesPolicy(HrManagementPolicies.ComplianceDsar, HrManagementRoles.Compliance, HrManagementRoles.PlatformAdmin);
            options.RolesPolicy(HrManagementPolicies.PlatformOperator, HrManagementRoles.PlatformAdmin);

            // PLAN-51 (§5, G-IAM-09): agency onboarding is the platform administrator's alone.
            options.RolesPolicy(HrManagementPolicies.PlatformAgencyOnboarding, HrManagementRoles.PlatformAdmin);

            // The base payroll role grants module access only. Operational roles are deliberately
            // separate so a processor cannot approve or post the same run.
            options.AddPolicy(HrManagementPolicies.PayrollProcess, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.PayrollProcessor)));

            options.AddPolicy(HrManagementPolicies.PayrollApprove, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.PayrollApprover)));

            // PLAN-51 (D-IAM-4): roles from configuration, so Authorization:AdminExcludedPolicies can drop hr-admin outside demo mode.
            options.RolesPolicy(HrManagementPolicies.PayrollPost, HrManagementRoles.Admin, HrManagementRoles.PayrollPoster);
            options.RolesPolicy(HrManagementPolicies.PayrollReverse,
                HrManagementRoles.Admin, HrManagementRoles.PayrollPoster, HrManagementRoles.PayrollReverser);

            // PLAN-50 (§5, rule 3): or an active delegate of hr-management-approver for the action's entity type. With no lookup registered
            // (the gateway, HR.Contracts' HireWizard.Step3Approve) it is the two roles only.
            options.AddPolicy(HrManagementPolicies.HrManagementApprove, p => p.AddRequirements(
                new Delegation.RoleOrDelegationRequirement(
                    [HrManagementRoles.Admin, HrManagementRoles.HrManagementApprover], HrManagementRoles.HrManagementApprover)));

            // PLAN-50 (D-IAM-1, D-MOV-2, D-50-3): the approval screens of the five movement types (and their delegates), and the movement
            // writes, made explicit.
            options.AddPolicy(HrManagementPolicies.HrMovementsApprovalAccess, p => p.AddRequirements(
                new Delegation.RoleOrDelegationRequirement(
                    [HrManagementRoles.Admin, HrManagementRoles.HrMovements, HrManagementRoles.HrManagementApprover],
                    HrManagementRoles.HrManagementApprover)));
            options.RolesPolicy(HrManagementPolicies.HrMovementsWrite, HrManagementRoles.Admin, HrManagementRoles.HrMovements);

            // PLAN-50 (§4): the gateway's coarse layer for the named approval paths.
            options.AddPolicy(HrManagementPolicies.GatewayApprovalReach, p => p.AddRequirements(new GatewayApprovalReachRequirement()));

            options.AddPolicy(HrManagementPolicies.HrContractsHireView, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || c.User.IsInRole(HrManagementRoles.HrContracts)
                || c.User.IsInRole(HrManagementRoles.HrCore)
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

            options.AddPolicy(HrManagementPolicies.PiiViewNationalId, p => p.RequireAssertion(c =>
                c.User.IsInRole(HrManagementRoles.Admin)
                || (!c.User.IsInRole(HrManagementRoles.BranchManager)
                    && (c.User.IsInRole(HrManagementRoles.HrCore)
                        || c.User.IsInRole(HrManagementRoles.HrCoreSupervisory)
                        || c.User.IsInRole(HrManagementRoles.HrCoreExecutive)))));
        });

        services.AddSingleton<IAuthorizationHandler, PolicyEngineRoleHandler>();
        services.AddSingleton<IAuthorizationHandler, GatewayApprovalReachHandler>();
        services.AddSingleton<IAuthorizationHandler, InteractiveStaffOrServiceScopeHandler>();
        services.AddSingleton<IAuthorizationHandler, ConfiguredRolesHandler>();
        services.AddSingleton<IAuthorizationHandler, Delegation.RoleOrDelegationHandler>();
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

    /// <summary>
    /// Requires the given module policy on all MVC actions (use after AddControllersWithViews), and PLAN-51 (§4): the module's write policy on
    /// a POST, PUT, PATCH or DELETE whose action names no policy of its own.
    /// </summary>
    public static IMvcBuilder AddHrModuleAuthorizeFilter(this IMvcBuilder mvc, string policyName, string writePolicyName)
    {
        mvc.Services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(o =>
            o.Filters.Add(new HrModuleAuthorizationFilter(policyName, writePolicyName)));
        // PLAN-50 (§1): a ModulePolicyOverride that names an unknown policy stops the boot.
        mvc.Services.AddHostedService<ModulePolicyOverrideValidator>();
        return mvc;
    }
}
