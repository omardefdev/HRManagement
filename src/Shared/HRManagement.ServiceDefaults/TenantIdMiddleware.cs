using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Extracts <c>tenant_id</c> from the authenticated principal into <see cref="TenantIdFlow"/>.
/// </summary>
public sealed class TenantIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Tenant-ID";
    public const string ItemKey = "TenantId";

    public async Task InvokeAsync(HttpContext context)
    {
        var claim = context.User.FindFirst(TenantContext.TenantClaimType)?.Value;
        Guid? tenantId = Guid.TryParse(claim, out var id) ? id : null;
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
}

public static class TenantIdMiddlewareExtensions
{
    public static IApplicationBuilder UseHrTenantId(this IApplicationBuilder app)
        => app.UseMiddleware<TenantIdMiddleware>();
}
