using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Preserves the peer address as seen on the socket, before <c>UseForwardedHeaders</c> replaces
/// <c>RemoteIpAddress</c> with a value taken from <c>X-Forwarded-For</c>.
/// <para>
/// Trust decisions such as "is this the local health probe" or "is this scrape internal" must use
/// the socket peer. The rewritten address reflects a header, so a trusted proxy relaying an
/// attacker-supplied value would otherwise be enough to claim to be loopback.
/// </para>
/// </summary>
public static class ConnectionOrigin
{
    private const string ItemsKey = "HRM.ConnectionOrigin.RemoteIp";

    /// <summary>Records the socket peer address. Must run before <c>UseForwardedHeaders</c>.</summary>
    public static IApplicationBuilder UseHrConnectionOrigin(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote is not null)
                context.Items[ItemsKey] = remote;
            await next();
        });

    /// <summary>
    /// The socket peer address when captured, otherwise the current (possibly rewritten) value.
    /// </summary>
    public static IPAddress? GetSocketRemoteIp(this HttpContext context) =>
        context.Items.TryGetValue(ItemsKey, out var value) && value is IPAddress address
            ? address
            : context.Connection.RemoteIpAddress;

    /// <summary>True when the request arrived on the loopback interface of this host.</summary>
    public static bool IsLoopbackConnection(this HttpContext context)
    {
        var remote = context.GetSocketRemoteIp();
        if (remote is null)
            return false;

        return IPAddress.IsLoopback(remote)
               || (context.Connection.LocalIpAddress is { } local && remote.Equals(local));
    }
}
