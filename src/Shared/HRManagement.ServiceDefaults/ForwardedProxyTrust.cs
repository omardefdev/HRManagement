using System.Net;
using Microsoft.Extensions.Configuration;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Resolves which peers may set <c>X-Forwarded-*</c>.
/// <para>
/// ASP.NET Core treats an empty <c>KnownProxies</c> plus an empty <c>KnownIPNetworks</c> as
/// "trust everyone", so a client can forge <c>X-Forwarded-For</c> and control
/// <c>RemoteIpAddress</c>. Every IP-keyed control (rate limiting, the metrics allow-list, the
/// loopback bypass on upstream health) then becomes bypassable, which is why the trusted set is
/// explicit and defaults to private ranges only.
/// </para>
/// Configure via <c>ForwardedHeaders:KnownProxies</c> (addresses) and
/// <c>ForwardedHeaders:KnownNetworks</c> (CIDR).
/// </summary>
public sealed record ForwardedProxyTrust(
    IReadOnlyList<IPAddress> Proxies,
    IReadOnlyList<IPNetwork> Networks,
    int ForwardLimit)
{
    /// <summary>Container and cluster ranges an edge proxy realistically sits in.</summary>
    public static readonly string[] DefaultNetworks =
    [
        "127.0.0.1/8",
        "::1/128",
        "10.0.0.0/8",
        "172.16.0.0/12",
        "192.168.0.0/16",
        "fd00::/8"
    ];

    public static ForwardedProxyTrust Resolve(IConfiguration configuration)
    {
        var section = configuration.GetSection("ForwardedHeaders");

        var proxies = new List<IPAddress>();
        foreach (var value in ReadList(section, "KnownProxies"))
        {
            if (IPAddress.TryParse(value, out var address))
                proxies.Add(address);
            else
                throw new InvalidOperationException(
                    $"ForwardedHeaders:KnownProxies contains an invalid IP address: '{value}'.");
        }

        var configuredNetworks = ReadList(section, "KnownNetworks");
        var networkValues = configuredNetworks.Count > 0 || proxies.Count > 0
            ? configuredNetworks
            : DefaultNetworks;

        var networks = new List<IPNetwork>();
        foreach (var value in networkValues)
        {
            networks.Add(ParseNetwork(value));
        }

        var forwardLimit = section.GetValue("ForwardLimit", 1);
        if (forwardLimit < 1)
            forwardLimit = 1;

        return new ForwardedProxyTrust(proxies, networks, forwardLimit);
    }

    private static IReadOnlyList<string> ReadList(IConfiguration section, string key)
    {
        var array = section.GetSection(key).Get<string[]>();
        if (array is { Length: > 0 })
            return array.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToList();

        var csv = section[key];
        if (string.IsNullOrWhiteSpace(csv))
            return [];

        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IPNetwork ParseNetwork(string value)
    {
        var parts = value.Split('/', 2);
        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out var prefix)
            || !int.TryParse(parts[1], out var length))
        {
            throw new InvalidOperationException(
                $"ForwardedHeaders:KnownNetworks contains an invalid CIDR range: '{value}'.");
        }

        return new IPNetwork(prefix, length);
    }
}
