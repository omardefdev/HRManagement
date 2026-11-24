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

/// <summary>
/// PLAN-54 (D-PAY-12): the requirement of <c>Payroll.LegalAttest</c>: the user meets the policy that
/// <see cref="PayrollLegalAttestPolicy.SettingKey"/> names.
/// </summary>
public sealed class PayrollLegalAttestRequirement : IAuthorizationRequirement;

/// <summary>Who attests a payroll framework version legally: the setting, its default, and the start-up check.</summary>
public static class PayrollLegalAttestPolicy
{
    public const string SettingKey = "Payroll:Legal:AttestorPolicy";
    public const string DefaultPolicy = HrManagementPolicies.PayrollApprove;

    public static string Configured(IConfiguration? configuration)
    {
        var value = configuration?[SettingKey];
        return string.IsNullOrWhiteSpace(value) ? DefaultPolicy : value.Trim();
    }

    /// <summary>
    /// Refuses a setting that would let nobody attest, or anybody: the named policy must be registered, and it cannot be
    /// <c>Payroll.LegalAttest</c> itself (the requirement would ask itself for ever). Called at the Payroll service's start.
    /// </summary>
    public static async Task ValidateAsync(IServiceProvider services)
    {
        var configuration = services.GetService<IConfiguration>();
        var name = Configured(configuration);
        if (string.Equals(name, HrManagementPolicies.PayrollLegalAttest, StringComparison.Ordinal))
            throw new InvalidOperationException($"{SettingKey} cannot name {HrManagementPolicies.PayrollLegalAttest} itself.");

        var policies = services.GetRequiredService<IAuthorizationPolicyProvider>();
        if (await policies.GetPolicyAsync(name) is null)
            throw new InvalidOperationException($"{SettingKey} names a policy that is not registered. Use the name of an existing policy, for example {DefaultPolicy}.");
    }
}

public sealed class PayrollLegalAttestHandler(IServiceProvider services) : AuthorizationHandler<PayrollLegalAttestRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PayrollLegalAttestRequirement requirement)
    {
        var name = PayrollLegalAttestPolicy.Configured(services.GetService<IConfiguration>());
        // A setting that names this policy itself is refused at start; it is never followed here.
        if (string.Equals(name, HrManagementPolicies.PayrollLegalAttest, StringComparison.Ordinal))
            return;

        // The authorization service is resolved when asked: it depends on the handlers, this one among them.
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var policy = await services.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync(name);
        if (policy is not null && (await authorization.AuthorizeAsync(context.User, context.Resource, policy)).Succeeded)
            context.Succeed(requirement);
    }
}
