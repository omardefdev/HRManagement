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
