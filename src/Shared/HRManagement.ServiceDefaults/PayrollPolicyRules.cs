using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// PLAN-53: who passes the payroll operational policies, written once. <c>Payroll.Process</c> and <c>Payroll.Approve</c> are role
/// assertions; <c>Payroll.MasterData</c> reads its roles from configuration (PLAN-51's <see cref="ConfiguredRolesRequirement"/>);
/// <c>Payroll.ChangeCancel</c> is the union of the three. The module role <c>payroll</c> alone passes none of them.
/// </summary>
public static class PayrollPolicyRules
{
    /// <summary>The requirement behind <c>Payroll.MasterData</c>: configured roles, by default hr-admin and payroll-processor.</summary>
    public static ConfiguredRolesRequirement MasterData { get; } = new(
        HrManagementPolicies.PayrollMasterData,
        ConfiguredRolesHandler.PolicyRolesKey(HrManagementPolicies.PayrollMasterData),
        [HrManagementRoles.Admin, HrManagementRoles.PayrollProcessor]);

    public static bool CanProcess(ClaimsPrincipal user) =>
        user.IsInRole(HrManagementRoles.Admin) || user.IsInRole(HrManagementRoles.PayrollProcessor);

    public static bool CanApprove(ClaimsPrincipal user) =>
        user.IsInRole(HrManagementRoles.Admin) || user.IsInRole(HrManagementRoles.PayrollApprover);

    public static bool CanChangeMasterData(ClaimsPrincipal user, IConfiguration? configuration) =>
        ConfiguredRolesHandler.IsSatisfied(user, configuration, MasterData);

    public static bool CanCancelChange(ClaimsPrincipal user, IConfiguration? configuration) =>
        CanProcess(user) || CanApprove(user) || CanChangeMasterData(user, configuration);
}

/// <summary>PLAN-53: the requirement of <c>Payroll.ChangeCancel</c> (see <see cref="PayrollPolicyRules.CanCancelChange"/>).</summary>
public sealed class PayrollChangeCancelRequirement : IAuthorizationRequirement;

public sealed class PayrollChangeCancelHandler(IServiceProvider services) : AuthorizationHandler<PayrollChangeCancelRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PayrollChangeCancelRequirement requirement)
    {
        if (PayrollPolicyRules.CanCancelChange(context.User, services.GetService<IConfiguration>()))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
