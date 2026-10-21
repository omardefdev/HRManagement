using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace HRManagement.ServiceDefaults;

/// <summary>IAM-002 org-scope denial captured for gateway audit (AC-03).</summary>
public sealed class OrgScopeDenialRecord
{
    public Guid? TenantId { get; init; }
    public Guid? ScopeOrgUnitId { get; init; }
    public string Reason { get; init; } = "org-scope-denied";
    public string? ResourceType { get; init; }
    public string? ResourceId { get; init; }
}

/// <summary>HttpContext.Items bridge from service controllers to gateway audit middleware.</summary>
public static class OrgScopeDenialContext
{
    public const string ItemKey = "OrgScopeDenial";

    public static void Record(
        HttpContext? http,
        ClaimsPrincipal user,
        string reason = "org-scope-denied",
        string? resourceType = null,
        string? resourceId = null,
        Guid? scopeOrgUnitId = null)
    {
        if (http is null)
            return;

        Guid? tenantId = null;
        var claim = user.FindFirst(TenantContext.TenantClaimType)?.Value;
        if (Guid.TryParse(claim, out var parsed))
            tenantId = parsed;

        http.Items[ItemKey] = new OrgScopeDenialRecord
        {
            TenantId = tenantId,
            ScopeOrgUnitId = scopeOrgUnitId ?? TryParseScopeOrgUnit(user),
            Reason = reason,
            ResourceType = resourceType,
            ResourceId = resourceId
        };
    }

    public static OrgScopeDenialRecord? Get(HttpContext? http) =>
        http?.Items.TryGetValue(ItemKey, out var value) == true && value is OrgScopeDenialRecord record
            ? record
            : null;

    private static Guid? TryParseScopeOrgUnit(ClaimsPrincipal user)
    {
        var value = user.FindFirst(HrOrgScopeVisibility.ScopeOrgUnitClaimType)?.Value;
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
