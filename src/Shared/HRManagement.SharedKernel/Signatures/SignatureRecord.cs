namespace HRManagement.SharedKernel.Signatures;

/// <summary>
/// Append-only legal digital signature record (SIG-001).
/// Standalone entity — not derived from BaseEntity — to enforce immutability.
/// </summary>
public class SignatureRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>Entity type being signed, e.g. "Contract", "Decision", "Payroll".</summary>
    public string SubjectType { get; set; } = string.Empty;

    /// <summary>Primary key of the signed entity.</summary>
    public string SubjectId { get; set; } = string.Empty;

    /// <summary>Identity claim of the signer (user principal / employee id).</summary>
    public string SignerSubject { get; set; } = string.Empty;

    /// <summary>Organizational role at the moment of signing.</summary>
    public string SignerRole { get; set; } = string.Empty;

    /// <summary>Legal intent expressed, e.g. "approve", "witness", "seal".</summary>
    public string Intent { get; set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>SHA-1 thumbprint of the X.509 certificate used.</summary>
    public string CertificateThumbprint { get; set; } = string.Empty;

    /// <summary>SHA-256 hash of the canonical (deterministic JSON) payload.</summary>
    public string CanonicalPayloadHash { get; set; } = string.Empty;

    /// <summary>Base64-encoded PKCS#1/PSS signature bytes.</summary>
    public string SignatureBase64 { get; set; } = string.Empty;

    /// <summary>Signature algorithm OID or friendly name, e.g. "SHA256withRSA".</summary>
    public string Algorithm { get; set; } = "SHA256withRSA";

    /// <summary>Long-Term Validation status: None, Pending, Valid, Expired, Revoked.</summary>
    public string LtvStatus { get; set; } = "None";
}
