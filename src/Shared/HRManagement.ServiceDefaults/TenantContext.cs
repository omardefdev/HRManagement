using System.Security.Claims;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Resolves tenant from JWT <c>tenant_id</c> claim, platform impersonation, or <see cref="TenantIdFlow"/>.
/// </summary>
public sealed class TenantContext(IHttpContextAccessor http) : ITenantContext
{
    public const string TenantClaimType = "tenant_id";

    public Guid? TenantId => ResolveTenantId();

    public bool IsPlatformOperator =>
        http.HttpContext?.User?.IsInRole(HrManagementRoles.PlatformAdmin) == true;

    public Guid RequireTenantId() =>
        TenantId ?? throw new InvalidOperationException("Tenant context required but not available.");

    private Guid? ResolveTenantId()
    {
        if (IsPlatformOperator)
            return ResolveImpersonatedTenantId();

        return GetFromClaim() ?? TenantIdFlow.Get();
    }

    private Guid? ResolveImpersonatedTenantId()
    {
        if (http.HttpContext?.Items.TryGetValue(PlatformTenantSessionMiddleware.ItemKey, out var sessionItem) == true
            && sessionItem is PlatformTenantSessionEntry session)
            return session.TenantId;

        var header = http.HttpContext?.Request.Headers[TenantIdMiddleware.HeaderName].FirstOrDefault();
        if (Guid.TryParse(header, out var headerId))
            return headerId;

        return TenantIdFlow.Get();
    }

    private Guid? GetFromClaim()
    {
        var value = http.HttpContext?.User?.FindFirst(TenantClaimType)?.Value;
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
