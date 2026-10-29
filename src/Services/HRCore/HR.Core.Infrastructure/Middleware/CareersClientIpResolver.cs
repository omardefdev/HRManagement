using System.Net;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace HR.Core.Infrastructure.Middleware;

/// <summary>
/// HCM-030 (PLAN-30 H3): client address of a <c>/careers</c> request, for the per-client OTP limiter and the hashed
/// <c>RequestIp</c>. HR.Core does not run <c>UseForwardedHeaders</c> (behind the gateway <c>RemoteIpAddress</c> is the
/// gateway), and turning it on globally would change every IP-keyed control. So: when <c>UseForwardedHeaders</c> is on,
/// the already-rewritten <c>RemoteIpAddress</c> is used; otherwise the right-most <c>X-Forwarded-For</c> entry (the one
/// the immediate proxy appended) is used only when that immediate peer is trusted by <see cref="ForwardedProxyTrust"/>
/// (<c>ForwardedHeaders:KnownProxies</c>/<c>KnownNetworks</c>, private ranges by default); any other peer is the client.
/// </summary>
public sealed class CareersClientIpResolver(ForwardedProxyTrust trust, bool forwardedHeadersApplied)
{
    public const string ForwardedForHeader = "X-Forwarded-For";

    public static CareersClientIpResolver FromConfiguration(IConfiguration configuration) =>
        new(ForwardedProxyTrust.Resolve(configuration), configuration.GetValue("UseForwardedHeaders", false));

    /// <summary>The client address, or null when the connection has none (in-memory test hosts).</summary>
    public IPAddress? Resolve(HttpContext context)
    {
        var peer = Normalize(context.Connection.RemoteIpAddress);
        if (peer is null || forwardedHeadersApplied || !IsTrusted(peer))
            return peer;

        return RightMostForwardedFor(context.Request.Headers[ForwardedForHeader]) ?? peer;
    }

    /// <summary><see cref="Resolve"/> as a string ("unknown" when there is no address).</summary>
    public string ResolveKey(HttpContext context) => Resolve(context)?.ToString() ?? "unknown";

    private bool IsTrusted(IPAddress peer) =>
        trust.Proxies.Any(p => Normalize(p)!.Equals(peer)) || trust.Networks.Any(n => n.Contains(peer));

    private static IPAddress? RightMostForwardedFor(Microsoft.Extensions.Primitives.StringValues values)
    {
        for (var i = values.Count - 1; i >= 0; i--)
        {
            var entries = (values[i] ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (entries.Length == 0)
                continue;
            return TryParseEndpoint(entries[^1]);
        }
        return null;
    }

    /// <summary>Parses <c>1.2.3.4</c>, <c>1.2.3.4:5678</c>, <c>::1</c> or <c>[::1]:5678</c>; null when it is not an address.</summary>
    private static IPAddress? TryParseEndpoint(string value)
    {
        if (IPAddress.TryParse(value, out var address))
            return Normalize(address);
        return IPEndPoint.TryParse(value, out var endpoint) ? Normalize(endpoint.Address) : null;
    }

    private static IPAddress? Normalize(IPAddress? address) =>
        address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4() : address;
}
