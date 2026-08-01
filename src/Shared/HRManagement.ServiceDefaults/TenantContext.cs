using System.Security.Claims;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Resolves tenant from JWT <c>tenant_id</c> claim, falling back to <see cref="TenantIdFlow"/>.
/// </summary>
public sealed class TenantContext(IHttpContextAccessor http) : ITenantContext
{
    public const string TenantClaimType = "tenant_id";

    public Guid? TenantId => GetFromClaim() ?? TenantIdFlow.Get();

    public bool IsPlatformOperator =>
        http.HttpContext?.User?.IsInRole(HrManagementRoles.PlatformAdmin) == true;

    public Guid RequireTenantId() =>
        TenantId ?? throw new InvalidOperationException("Tenant context required but not available.");

    private Guid? GetFromClaim()
    {
        var value = http.HttpContext?.User?.FindFirst(TenantClaimType)?.Value;
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
