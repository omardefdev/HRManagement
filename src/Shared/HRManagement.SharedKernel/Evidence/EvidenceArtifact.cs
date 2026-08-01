namespace HRManagement.SharedKernel.Evidence;

/// <summary>
/// EVD-003 immutable evidence artifact metadata (append-only; no UpdatedAt).
/// </summary>
public class EvidenceArtifact
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Human-readable artifact identifier.</summary>
    public string ArtifactId { get; set; } = "";

    public string CardId { get; set; } = "";

    public string? ControlId { get; set; }

    public string ReleaseDigest { get; set; } = "";

    /// <summary>CI / Test / Drill / Manual (or other source labels).</summary>
    public string SourceType { get; set; } = "";

    /// <summary>SHA-256 hex of content bytes.</summary>
    public string ContentHash { get; set; } = "";

    public string? Signature { get; set; }

    public DateTime TrustedTimestampUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ExpiresAtUtc { get; set; }

    public string Classification { get; set; } = "Internal";

    /// <summary>MinIO path or local storage URI.</summary>
    public string StorageUri { get; set; } = "";

    public bool IsExpired => ExpiresAtUtc.HasValue && ExpiresAtUtc.Value <= DateTime.UtcNow;
}
