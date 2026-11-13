using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HRManagement.SharedKernel.Exports;
using HRManagement.SharedKernel.Tenancy;

namespace HRManagement.ServiceDefaults;

public static class ExportJobAuth
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static ExportAuthSnapshot Capture(
        ClaimsPrincipal user,
        ITenantContext? tenantContext,
        Guid? scopeOrgUnitId = null,
        IReadOnlyCollection<Guid>? allowedOrgUnitIds = null)
    {
        var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        _ = Guid.TryParse(userIdStr, out var userId);

        var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string? hash = null;
        if (allowedOrgUnitIds is { Count: > 0 })
        {
            var ordered = string.Join(',', allowedOrgUnitIds.OrderBy(x => x));
            hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ordered)));
        }

        return new ExportAuthSnapshot
        {
            UserId = userId,
            TenantId = tenantContext?.TenantId ?? Guid.Empty,
            ScopeOrgUnitId = scopeOrgUnitId,
            ScopeIdsHash = hash,
            Roles = roles
        };
    }

    /// <summary>PLAN-52 (P-7): records the requester's scope in the snapshot: unrestricted, restricted with its root, or denied.</summary>
    public static ExportAuthSnapshot WithScope(ExportAuthSnapshot snapshot, OrgAccessScope scope, ClaimsPrincipal user)
    {
        snapshot.ScopeKind = scope.IsDenied ? ExportScopeKinds.Denied : scope.MustFilter ? ExportScopeKinds.Restricted : ExportScopeKinds.Unrestricted;
        if (scope.MustFilter && !scope.IsDenied)
            snapshot.ScopeOrgUnitId = HrOrgScopeVisibility.GetScopeRootOrgUnitId(user);
        return snapshot;
    }

    /// <summary>
    /// PLAN-52 (P-7): the scope a job applies, rebuilt from its snapshot. A restricted requester's job whose scope was not recorded (queued
    /// before PLAN-52, or a restricted kind without a root) exports nothing (fail-closed): denied.
    /// </summary>
    public static async Task<OrgAccessScope> RecordedScopeAsync(
        ExportAuthSnapshot? snapshot,
        IQueryable<HRManagement.SharedKernel.Entities.OrgUnitSnapshot> units,
        CancellationToken ct = default)
    {
        switch (snapshot?.ScopeKind)
        {
            case ExportScopeKinds.Unrestricted:
                return OrgAccessScope.Unrestricted();
            case ExportScopeKinds.Restricted when snapshot.ScopeOrgUnitId is Guid root:
                return await OrgScopeResolver.GetAllowedForRootAsync(root, units, ct);
            case null when snapshot is not null:
                // No scope recorded: the requester's roles tell whether he was restricted (D-IAM-3's one definition).
                var principal = new ClaimsPrincipal(new ClaimsIdentity(
                    snapshot.Roles.Select(r => new Claim(ClaimTypes.Role, r)), "export-snapshot", ClaimTypes.Name, ClaimTypes.Role));
                return HrOrgScopeVisibility.IsBranchScoped(principal) ? OrgAccessScope.Denied() : OrgAccessScope.Unrestricted();
            default:
                return OrgAccessScope.Denied();
        }
    }

    public static string Serialize(ExportAuthSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);

    public static ExportAuthSnapshot? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        return JsonSerializer.Deserialize<ExportAuthSnapshot>(json, JsonOptions);
    }

    /// <summary>Re-check download: same user + tenant; scope hash must match when present.</summary>
    public static bool MatchesCurrentUser(
        ExportAuthSnapshot snapshot,
        ClaimsPrincipal user,
        ITenantContext? tenantContext,
        IReadOnlyCollection<Guid>? currentAllowedOrgUnitIds = null)
    {
        var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        if (!Guid.TryParse(userIdStr, out var userId) || userId != snapshot.UserId)
            return false;

        var tenantId = tenantContext?.TenantId ?? Guid.Empty;
        if (snapshot.TenantId != Guid.Empty && tenantId != Guid.Empty && snapshot.TenantId != tenantId)
            return false;

        if (!string.IsNullOrEmpty(snapshot.ScopeIdsHash) && currentAllowedOrgUnitIds is not null)
        {
            var ordered = string.Join(',', currentAllowedOrgUnitIds.OrderBy(x => x));
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ordered)));
            if (!string.Equals(hash, snapshot.ScopeIdsHash, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
