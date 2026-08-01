using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using HRManagement.SharedKernel.Signatures;

namespace HRManagement.ServiceDefaults.Signatures;

/// <summary>
/// Stateless signature verification helper (SIG-001).
/// Can be used independently of the signing service for audit / external tooling.
/// </summary>
public static class SignatureVerifier
{
    /// <summary>Verify a <see cref="SignatureRecord"/> against the provided X.509 certificate.</summary>
    public static bool Verify(SignatureRecord record, X509Certificate2 certificate)
    {
        using var rsa = certificate.GetRSAPublicKey();
        if (rsa is null) return false;

        var dataBytes = Encoding.UTF8.GetBytes(record.CanonicalPayloadHash);
        var signatureBytes = Convert.FromBase64String(record.SignatureBase64);

        return rsa.VerifyData(dataBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    /// <summary>
    /// Re-compute the canonical hash for <paramref name="payload"/> and check it matches the record,
    /// then cryptographically verify the signature.
    /// </summary>
    public static bool VerifyWithPayload(SignatureRecord record, object payload, X509Certificate2 certificate)
    {
        var expectedHash = CanonicalPayloadBuilder.ComputeHash(payload);
        if (!string.Equals(record.CanonicalPayloadHash, expectedHash, StringComparison.Ordinal))
            return false;

        return Verify(record, certificate);
    }
}
