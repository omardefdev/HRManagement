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

        var controller = context.RouteData.Values["controller"]?.ToString() ?? "";
        if (controller.StartsWith("Ess", StringComparison.OrdinalIgnoreCase)
            || string.Equals(controller, "EssPortal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(controller, "LookupApi", StringComparison.OrdinalIgnoreCase)
            // IAM-004: S2S endpoints authorize via Service.* policies, not module roles.
            || string.Equals(controller, "DirectoryInternalSync", StringComparison.OrdinalIgnoreCase))
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
