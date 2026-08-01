using System.Security.Cryptography;
using System.Text;

namespace HRManagement.SharedKernel.Evidence;

/// <summary>SHA-256 sealing helpers for <see cref="EvidenceRecord"/> (EVD-001).</summary>
public static class EvidenceHashChain
{
    public const string DefaultChainKey = "default";
    public const string GenesisPrevHash = "GENESIS";

    public static string ComputeEventHash(EvidenceRecord e, string prevHash)
    {
        var payload = string.Join('|',
            e.Id.ToString("N"),
            e.TenantId.ToString("N"),
            e.CorrelationId,
            e.CausationId ?? "",
            e.Actor,
            e.Subject ?? "",
            e.Resource,
            e.Purpose,
            e.Result,
            e.BeforeHash ?? "",
            e.AfterHash ?? "",
            e.OccurredAtUtc.ToUnixTimeMilliseconds().ToString(),
            e.Sequence.ToString(),
            prevHash,
            e.ChainKey,
            e.ReleaseVersion ?? "",
            e.ConfigVersion ?? "");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static void Seal(EvidenceRecord e, string prevHash)
    {
        e.PrevHash = prevHash;
        e.EventHash = ComputeEventHash(e, prevHash);
    }

    public static bool Verify(EvidenceRecord e) =>
        string.Equals(e.EventHash, ComputeEventHash(e, e.PrevHash), StringComparison.Ordinal);
}
