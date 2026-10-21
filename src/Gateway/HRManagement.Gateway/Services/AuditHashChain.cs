using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HRManagement.Gateway.Models;

namespace HRManagement.Gateway.Services;

/// <summary>OBS-003: SHA-256 hash chain over immutable gateway audit fields.</summary>
public static class AuditHashChain
{
    public const string DefaultChainKey = "default";
    public const string GenesisPrevHash = "GENESIS";

    public static string ComputeEventHash(GatewayAuditEntry e, string prevHash)
    {
        // Canonical payload — order matters; do not include Id (assigned after insert).
        var payload = string.Join('|',
            e.ChainKey ?? DefaultChainKey,
            prevHash,
            e.SequenceNumber.ToString(CultureInfo.InvariantCulture),
            e.OccurredAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            e.CorrelationId ?? "",
            e.UserSub ?? "",
            e.Method ?? "",
            e.Path ?? "",
            e.QueryString ?? "",
            e.StatusCode.ToString(CultureInfo.InvariantCulture),
            e.ElapsedMs.ToString(CultureInfo.InvariantCulture),
            e.Decision ?? "",
            e.ClientIp ?? "",
            e.TenantId?.ToString("D") ?? "",
            e.ScopeOrgUnitId?.ToString("D") ?? "",
            e.Reason ?? "",
            e.ResourceType ?? "",
            e.ResourceId ?? "");

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static void Seal(GatewayAuditEntry e, string prevHash)
    {
        e.ChainKey ??= DefaultChainKey;
        e.PrevHash = prevHash;
        e.EventHash = ComputeEventHash(e, prevHash);
    }

    public static bool Verify(GatewayAuditEntry e)
    {
        if (string.IsNullOrEmpty(e.EventHash) || string.IsNullOrEmpty(e.PrevHash))
            return false;
        var expected = ComputeEventHash(e, e.PrevHash);
        if (expected.Length != e.EventHash.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(e.EventHash));
    }
}
