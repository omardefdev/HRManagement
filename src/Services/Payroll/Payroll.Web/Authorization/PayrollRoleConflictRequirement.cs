using System.Text.Json;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.SeparationOfDuties;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Payroll.Application.Options;

namespace Payroll.Web.Authorization;

/// <summary>
/// PLAN-58 (§5, D-PAY-20, P-9): no token that carries two conflicting payroll roles passes a financial act. Added in Payroll to
/// <c>Payroll.Process</c>, <c>Payroll.Approve</c>, <c>Payroll.Post</c>, <c>Payroll.Reverse</c> and <c>Payroll.AuditReview</c>. The pairs are the
/// separation-of-duties matrix's (<see cref="SodMatrixDefaults"/>: processor/approver, approver/poster, processor/poster). <c>hr-admin</c>
/// counts as holding every payroll role whose policy it passes: processor and approver everywhere, and poster where the posting's policy
/// keeps it (the demo, a lab). Roles given in Keycloak directly meet this check too.
/// </summary>
public sealed class PayrollRoleConflictRequirement(string policyName) : IAuthorizationRequirement
{
    public string PolicyName { get; } = policyName;

    /// <summary>The five policies of a financial act that carry the requirement.</summary>
    public static readonly IReadOnlyList<string> FinancialPolicies =
    [
        HrManagementPolicies.PayrollProcess,
        HrManagementPolicies.PayrollApprove,
        HrManagementPolicies.PayrollPost,
        HrManagementPolicies.PayrollReverse,
        HrManagementPolicies.PayrollAuditReview,
    ];

    /// <summary>The Arabic refusal: names the conflicting pair (its roles and the matrix's reason).</summary>
    public static string Message(string roleA, string roleB, string? reason) =>
        $"رُفض الفعل: رمز دخولك يحمل دورين متعارضين ({roleA} و{roleB})" +
        (string.IsNullOrWhiteSpace(reason) ? "" : $" — {reason}") +
        ". لا يُنفَّذ فعل مالي حتى يُصحَّح إسناد أدوارك (فصل المهام).";
}

/// <summary>The refusal carried to the authorization result, so the answer can name the pair (403 with the message).</summary>
public sealed class PayrollRoleConflictFailure(IAuthorizationHandler handler, string message) : AuthorizationFailureReason(handler, message);

public sealed class PayrollRoleConflictHandler(
    SeparationOfDutiesService sod,
    IOptionsMonitor<PayrollSodOptions> options,
    IAuthorizationPolicyProvider policies,
    Microsoft.Extensions.Configuration.IConfiguration? configuration = null) : AuthorizationHandler<PayrollRoleConflictRequirement>
{
    private static readonly string[] PayrollRoles =
    [
        HrManagementRoles.PayrollProcessor, HrManagementRoles.PayrollApprover, HrManagementRoles.PayrollPoster, HrManagementRoles.PayrollReverser,
    ];

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PayrollRoleConflictRequirement requirement)
    {
        var roles = await EffectiveRolesAsync(context, requirement);
        var pair = FirstConflict(roles);
        if (pair is null)
        {
            context.Succeed(requirement);
            return;
        }

        var (a, b, reason) = pair.Value;
        var reject = options.CurrentValue.RejectConflictingRoleHolders;
        // The audit line is written for the act's own authorization (the endpoint's), not for a page that asks whether to show a button.
        if (context.Resource is HttpContext http)
            Audit(http, requirement.PolicyName, a, b, reject);

        if (reject)
            context.Fail(new PayrollRoleConflictFailure(this, PayrollRoleConflictRequirement.Message(a, b, reason)));
        else
            context.Succeed(requirement);
    }

    /// <summary>The payroll roles the token holds, hr-admin counted as each role whose policy it passes.</summary>
    private async Task<HashSet<string>> EffectiveRolesAsync(AuthorizationHandlerContext context, PayrollRoleConflictRequirement requirement)
    {
        var user = context.User;
        var roles = PayrollRoles.Where(user.IsInRole).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!user.IsInRole(HrManagementRoles.Admin))
            return roles;

        // Payroll.Process and Payroll.Approve admit hr-admin everywhere (PayrollPolicyRules); Payroll.Post keeps it where the setting
        // does not exclude it (Authorization:AdminExcludedPolicies outside demo mode, PLAN-51).
        roles.Add(HrManagementRoles.PayrollProcessor);
        roles.Add(HrManagementRoles.PayrollApprover);
        if (await AdminPassesAsync(HrManagementPolicies.PayrollPost, context))
            roles.Add(HrManagementRoles.PayrollPoster);
        return roles;
    }

    private async Task<bool> AdminPassesAsync(string policyName, AuthorizationHandlerContext context)
    {
        _ = context;
        var policy = await policies.GetPolicyAsync(policyName);
        var configured = policy?.Requirements.OfType<ConfiguredRolesRequirement>().FirstOrDefault();
        return configured is not null
               && ConfiguredRolesHandler.Roles(configuration, configured).Contains(HrManagementRoles.Admin, StringComparer.OrdinalIgnoreCase);
    }

    private (string A, string B, string? Reason)? FirstConflict(IReadOnlyCollection<string> roles)
    {
        var ordered = roles.Order(StringComparer.Ordinal).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            for (var j = i + 1; j < ordered.Count; j++)
            {
                if (sod.RolesConflict(ordered[i], ordered[j], out var reason))
                    return (ordered[i], ordered[j], reason);
            }
        }

        return null;
    }

    private static void Audit(HttpContext http, string policyName, string roleA, string roleB, bool rejected)
    {
        var endpoint = (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? http.GetEndpoint()?.DisplayName ?? "unknown";
        SecurityAudit.Log(
            http,
            SecurityAudit.EventTypes.RoleConflict,
            $"policy:{policyName}",
            JsonSerializer.Serialize(new { pair = $"{roleA}/{roleB}", policy = policyName, endpoint, rejected }));
    }
}

/// <summary>
/// PLAN-58 (P-4): a refusal of the conflict check answers 403 with its Arabic message naming the pair (JSON on the API, a short page on a
/// screen); every other result goes to the default handler.
/// </summary>
public sealed class PayrollRoleConflictResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        var conflict = authorizeResult.Forbidden
            ? authorizeResult.AuthorizationFailure?.FailureReasons.OfType<PayrollRoleConflictFailure>().FirstOrDefault()
            : null;
        if (conflict is null)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.Value?.Contains("/api/", StringComparison.OrdinalIgnoreCase) == true)
        {
            await context.Response.WriteAsJsonAsync(new { error = conflict.Message });
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(
            "<!DOCTYPE html><html lang=\"ar\" dir=\"rtl\"><head><meta charset=\"utf-8\"><title>فصل المهام</title></head><body><p>"
            + System.Net.WebUtility.HtmlEncode(conflict.Message) + "</p></body></html>");
    }
}

/// <summary>PLAN-58 (P-9): adds the conflict check to Payroll's five financial policies, and its 403 answer.</summary>
public static class PayrollRoleConflictRegistration
{
    public static IServiceCollection AddPayrollRoleConflictCheck(this IServiceCollection services)
    {
        services.Configure<AuthorizationOptions>(options =>
        {
            foreach (var name in PayrollRoleConflictRequirement.FinancialPolicies)
            {
                var existing = options.GetPolicy(name)
                    ?? throw new InvalidOperationException($"The policy {name} is not registered before the conflict check.");
                options.AddPolicy(name, new AuthorizationPolicyBuilder(existing).AddRequirements(new PayrollRoleConflictRequirement(name)).Build());
            }
        });
        services.AddSingleton<IAuthorizationHandler, PayrollRoleConflictHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, PayrollRoleConflictResultHandler>();
        return services;
    }
}
