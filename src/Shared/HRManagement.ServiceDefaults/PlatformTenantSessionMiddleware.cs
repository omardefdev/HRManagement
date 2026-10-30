using System.Security.Claims;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Loads platform-admin tenant impersonation into HttpContext before tenant resolution (AC-05).
/// </summary>
public sealed class PlatformTenantSessionMiddleware(RequestDelegate next)
{
    public const string ItemKey = "PlatformTenantSession";

    public async Task InvokeAsync(HttpContext context, IPlatformTenantSessionStore sessionStore)
    {
        if (context.User.IsInRole(HrManagementRoles.PlatformAdmin))
        {
            var sub = context.User.FindFirstValue("sub")
                ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier); if (!string.IsNullOrEmpty(sub))
            {
                var session = sessionStore.Get(sub);
                if (session is not null)
                    context.Items[ItemKey] = session;
            }
        }

        await next(context);
    }
}

public static class PlatformTenantSessionMiddlewareExtensions
{
    public static IApplicationBuilder UsePlatformTenantSession(this IApplicationBuilder app) =>
        app.UseMiddleware<PlatformTenantSessionMiddleware>();
}
