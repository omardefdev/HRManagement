using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Applies the module policy to every MVC action of a service, and its write policy to a write that names no policy of its own.
/// <para>
/// PLAN-51 (§4, P-6): no controller is exempt by its name any more. An action or controller that must not meet the module policy says so
/// with <see cref="ModulePolicyOverrideAttribute"/> (naming the policy its actions already require), or carries a <c>Service.*</c> policy.
/// A POST, PUT, PATCH or DELETE on an action with no explicit policy on the action or its class then also meets the module's write policy,
/// after whichever policy applied (the override included). Every write names its policy anyway
/// (<c>WriteActionsHaveExplicitPolicyTests</c>); this is the safety net, never the only guard.
/// </para>
/// </summary>
public sealed class HrModuleAuthorizationFilter(string modulePolicy, string? writePolicy = null) : IAsyncAuthorizationFilter
{
    public string ModulePolicy { get; } = modulePolicy;

    public string? WritePolicy { get; } = writePolicy;

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

        // PLAN-50 (§1): an action (or its controller) may name the policy that replaces the module's.
        var policy = ModulePolicyOverrideAttribute.For(context.ActionDescriptor)?.Policy ?? ModulePolicy;
        if (!await AuthorizeAsync(context, policy))
            return;

        // PLAN-51 (§4, P-6): the default write policy, for a write that names no policy of its own.
        if (WritePolicy is not null
            && IsWrite(context.HttpContext.Request.Method)
            && !context.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any(a => !string.IsNullOrWhiteSpace(a.Policy)))
            await AuthorizeAsync(context, WritePolicy);
    }

    public static bool IsWrite(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    private static async Task<bool> AuthorizeAsync(AuthorizationFilterContext context, string policy)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            context.Result = new ChallengeResult();
            return false;
        }

        // The HttpContext is the resource, as for an [Authorize] attribute, so a requirement can read the endpoint's metadata.
        var auth = context.HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();
        var result = await auth.AuthorizeAsync(context.HttpContext.User, context.HttpContext, policy);
        if (result.Succeeded)
            return true;

        context.Result = new ForbidResult();
        return false;
    }
}
