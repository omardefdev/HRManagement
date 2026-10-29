using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Applies the module policy globally, except for ESS controllers that define their own <see cref="HrManagementPolicies.ModuleEss"/> policy,
/// and LookupApi which uses <see cref="HrManagementPolicies.HrCoreLookup"/> (any authenticated user).
/// </summary>
public sealed class HrModuleAuthorizationFilter(string modulePolicy) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // Endpoints explicitly marked [AllowAnonymous] (e.g. public citizen portal) bypass the module policy.
        if (context.ActionDescriptor.EndpointMetadata.Any(m => m is IAllowAnonymous))
            return;

        // Service endpoints carry their own client/scope policy. Requiring an interactive
        // module role as well would make every valid client_credentials call impossible.
        if (context.ActionDescriptor.EndpointMetadata
            .OfType<IAuthorizeData>()
            .Any(m => m.Policy?.StartsWith("Service.", StringComparison.Ordinal) == true))
            return;

        var controller = context.RouteData.Values["controller"]?.ToString() ?? "";
        if (controller.StartsWith("Ess", StringComparison.OrdinalIgnoreCase)
            || string.Equals(controller, "EssPortal", StringComparison.OrdinalIgnoreCase)
            // The hire wizard has explicit policies per stage: management approval at Step3
            // and contracts approval at Step4. Do not override those with the broad module role.
            || string.Equals(controller, "HireWizard", StringComparison.OrdinalIgnoreCase)
            || string.Equals(controller, "LookupApi", StringComparison.OrdinalIgnoreCase)
            // IAM-004: S2S API controllers carry their own Service.* / HrCoreWrite policies.
            || string.Equals(controller, "CodeGeneration", StringComparison.OrdinalIgnoreCase)
            || string.Equals(controller, "Policies", StringComparison.OrdinalIgnoreCase)
            || string.Equals(controller, "Onboarding", StringComparison.OrdinalIgnoreCase)
            || string.Equals(controller, "TenantQuarantine", StringComparison.OrdinalIgnoreCase)
            || string.Equals(controller, "EmployeeLifecycleInternal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(controller, "DirectoryInternalSync", StringComparison.OrdinalIgnoreCase)
            // HCM-028 (PLAN-25 slice): HrCoreAppealReview / HrCoreWorkInbox admit the compliance reviewer.
            || string.Equals(controller, "RecruitmentAppeals", StringComparison.OrdinalIgnoreCase)
            || string.Equals(controller, "WorkInbox", StringComparison.OrdinalIgnoreCase)
            // HCM-031 (PLAN-31): the marking controller carries HrCoreExamMarking (the exam marker role only).
            || string.Equals(controller, "ExamMarking", StringComparison.OrdinalIgnoreCase))
            return;

        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            context.Result = new Microsoft.AspNetCore.Mvc.ChallengeResult();
            return;
        }

        var auth = context.HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();
        var result = await auth.AuthorizeAsync(context.HttpContext.User, modulePolicy);
        if (!result.Succeeded)
            context.Result = new ForbidResult();
    }
}
