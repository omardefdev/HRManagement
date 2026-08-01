using System.Security.Claims;
using HRManagement.SharedKernel.Files;

namespace HRManagement.ServiceDefaults.Files;

/// <summary>
/// SEC-003: result of an object-level file authorization decision.
/// </summary>
public sealed record FileAccessDecision(bool Allowed, string Reason)
{
    public static FileAccessDecision Allow(string reason = "Granted") => new(true, reason);
    public static FileAccessDecision Deny(string reason) => new(false, reason);
}

/// <summary>
/// SEC-003: object-level file authorization policy interface.
/// Every download/delete/reference must pass through this policy — the caller
/// cannot distinguish between 403 and "file does not exist" (no info leakage).
/// </summary>
public interface IFileAccessPolicy
{
    /// <summary>Evaluate whether the principal may access the file described by the grant.</summary>
    FileAccessDecision Evaluate(ClaimsPrincipal user, FileAccessGrant grant, Guid requestTenantId);

    /// <summary>Evaluate whether the principal may delete the file.</summary>
    FileAccessDecision EvaluateDelete(ClaimsPrincipal user, FileAccessGrant grant, Guid requestTenantId);
}

/// <summary>
/// SEC-003: default file access policy enforcing tenant isolation, module ownership,
/// holder match, and revocation checks. Cross-tenant access is always denied.
/// </summary>
public sealed class FileAccessPolicy : IFileAccessPolicy
{
    public FileAccessDecision Evaluate(ClaimsPrincipal user, FileAccessGrant grant, Guid requestTenantId)
    {
        if (grant.IsRevoked)
            return FileAccessDecision.Deny("Grant revoked");

        if (grant.TenantId != requestTenantId)
            return FileAccessDecision.Deny("Cross-tenant access denied");

        if (user.IsInRole(HrManagementRoles.Admin) || user.IsInRole(HrManagementRoles.PlatformAdmin))
            return FileAccessDecision.Allow("Admin role");

        if (grant.Visibility == FileVisibility.Public)
            return FileAccessDecision.Allow("Public visibility");

        if (grant.Visibility == FileVisibility.Tenant && user.Identity?.IsAuthenticated == true)
            return FileAccessDecision.Allow("Tenant-wide visibility");

        if (grant.Visibility == FileVisibility.Module && IsInModule(user, grant.Module))
            return FileAccessDecision.Allow("Module access");

        if (grant.GranteeId is not null && IsGrantee(user, grant.GranteeId))
            return FileAccessDecision.Allow("Explicit grantee");

        if (IsHolder(user, grant))
            return FileAccessDecision.Allow("Holder access");

        return FileAccessDecision.Deny("No matching access rule");
    }

    public FileAccessDecision EvaluateDelete(ClaimsPrincipal user, FileAccessGrant grant, Guid requestTenantId)
    {
        if (grant.TenantId != requestTenantId)
            return FileAccessDecision.Deny("Cross-tenant delete denied");

        if (user.IsInRole(HrManagementRoles.Admin) || user.IsInRole(HrManagementRoles.PlatformAdmin))
            return FileAccessDecision.Allow("Admin delete");

        if (user.IsInRole(HrManagementRoles.FileStorage))
            return FileAccessDecision.Allow("FileStorage service role");

        return FileAccessDecision.Deny("Delete restricted to owning service or admin");
    }

    private static bool IsInModule(ClaimsPrincipal user, string module)
    {
        var roleSuffix = module.Replace(".", "").Replace(" ", "").ToLowerInvariant();
        return user.Claims.Any(c =>
            c.Type == ClaimTypes.Role &&
            c.Value.Contains(roleSuffix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsGrantee(ClaimsPrincipal user, string granteeId) =>
        user.FindFirst("sub")?.Value == granteeId
        || user.FindFirst(ClaimTypes.NameIdentifier)?.Value == granteeId;

    private static bool IsHolder(ClaimsPrincipal user, FileAccessGrant grant)
    {
        var employeeId = HrOrgScopeVisibility.GetEmployeeId(user);
        return employeeId.HasValue && grant.HolderId == employeeId.Value.ToString();
    }
}
