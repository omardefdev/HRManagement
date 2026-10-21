using System.Security.Cryptography;
using System.Text;

namespace HRManagement.ServiceDefaults.Seeding;

/// <summary>Fingerprint of the canonical topology and the canonical persona source.</summary>
public static class AcceptanceSeedPackFingerprint
{
    public static string Value { get; } = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{AcceptanceSeedCatalog.Version}|{AcceptanceSeedCatalog.ReleaseDigest}|{AcceptancePersonaCatalog.ReleaseDigest}")))
        .ToLowerInvariant();
}
