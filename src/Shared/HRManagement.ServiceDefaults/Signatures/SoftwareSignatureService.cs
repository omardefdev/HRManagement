using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using HRManagement.SharedKernel.Signatures;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.Signatures;

/// <summary>
/// Software-based X.509 signing service (SIG-001).
/// Uses a PFX certificate loaded from disk or cert store.
/// Replace with an HSM-backed implementation when hardware is available.
/// </summary>
public sealed class SoftwareSignatureService : ISignatureService, IDisposable
{
    private readonly X509Certificate2 _certificate;

    public SoftwareSignatureService(IOptions<SignatureOptions> options)
    {
        var opts = options.Value;
        _certificate = string.IsNullOrEmpty(opts.CertificatePath)
            ? CreateSelfSignedDev()
            : X509CertificateLoader.LoadPkcs12FromFile(
                  opts.CertificatePath,
                  opts.CertificatePassword,
                  X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.EphemeralKeySet);
    }

    public Task<SignatureRecord> SignAsync(SignatureRequest request, CancellationToken cancellationToken = default)
    {
        using var rsa = _certificate.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("Certificate does not contain an RSA private key.");

        var dataBytes = Encoding.UTF8.GetBytes(request.CanonicalPayloadHash);
        var signatureBytes = rsa.SignData(dataBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var record = new SignatureRecord
        {
            TenantId = request.TenantId,
            SubjectType = request.SubjectType,
            SubjectId = request.SubjectId,
            SignerSubject = request.SignerSubject,
            SignerRole = request.SignerRole,
            Intent = request.Intent,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            CertificateThumbprint = _certificate.Thumbprint,
            CanonicalPayloadHash = request.CanonicalPayloadHash,
            SignatureBase64 = Convert.ToBase64String(signatureBytes),
            Algorithm = "SHA256withRSA",
            LtvStatus = "None",
        };

        return Task.FromResult(record);
    }

    public bool Verify(SignatureRecord record)
    {
        if (record.CertificateThumbprint != _certificate.Thumbprint)
            return false;

        return SignatureVerifier.Verify(record, _certificate);
    }

    public void Dispose() => _certificate.Dispose();

    private static X509Certificate2 CreateSelfSignedDev()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=HRManagement-SIG001-Dev", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(2));
    }
}

public sealed class SignatureOptions
{
    public const string SectionName = "LegalSignatures";
    public string? CertificatePath { get; set; }
    public string? CertificatePassword { get; set; }
}
