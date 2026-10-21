using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Extracts tenant into <see cref="TenantIdFlow"/> from JWT, platform impersonation, or gateway header.
/// </summary>
public sealed class TenantIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Tenant-ID";
    public const string ItemKey = "TenantId";

    public async Task InvokeAsync(HttpContext context)
    {
        Guid? tenantId = ResolveTenantId(context);
        TenantIdFlow.Set(tenantId);

        if (tenantId.HasValue)
            context.Items[ItemKey] = tenantId.Value;

        try
        {
            await next(context);
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    internal static Guid? ResolveTenantId(HttpContext context)
    {
        if (context.User.IsInRole(HrManagementRoles.PlatformAdmin))
        {
            if (context.Items.TryGetValue(PlatformTenantSessionMiddleware.ItemKey, out var sessionItem)
                && sessionItem is PlatformTenantSessionEntry session)
                return session.TenantId;

            return null;
        }

        var claim = context.User.FindFirst(TenantContext.TenantClaimType)?.Value;
        if (Guid.TryParse(claim, out var id))
            return id;

        if (context.Request.Headers.TryGetValue(HeaderName, out var headerValues)
            && Guid.TryParse(headerValues.FirstOrDefault(), out var headerId))
            return headerId;

        return null;
    }
}

public static class TenantIdMiddlewareExtensions
{
    public static IApplicationBuilder UseHrTenantId(this IApplicationBuilder app)
        => app.UseMiddleware<TenantIdMiddleware>();
}
