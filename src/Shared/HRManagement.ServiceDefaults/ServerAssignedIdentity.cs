using System.Security.Claims;

namespace HRManagement.ServiceDefaults;

/// <summary>Identity fields the server assigns from the authenticated session (SEC-001).</summary>
public static class ServerAssignedIdentity
{
    /// <summary>Actor label for server-side inbound/event handlers with no interactive user.</summary>
    public const string InboundSystemActor = "system:inbound";

    public static string ResolveActorName(ClaimsPrincipal user) =>
        user.Identity?.Name
        ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? user.FindFirstValue("sub")
        ?? "unknown";

    public static Guid? ResolveActorEmployeeId(ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
