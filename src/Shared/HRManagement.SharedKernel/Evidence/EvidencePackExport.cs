namespace HRManagement.SharedKernel.Evidence;

/// <summary>
/// EVD-003 export of an evidence pack (append-only audit of pack generation).
/// </summary>
public class EvidencePackExport
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string PackId { get; set; } = "";

    public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;

    public string ExportedBy { get; set; } = "";

    /// <summary>JSON array of included artifact ids.</summary>
    public string ArtifactIdsJson { get; set; } = "[]";

    /// <summary>SHA-256 over sorted content hashes of included artifacts.</summary>
    public string PackHash { get; set; } = "";

    public string Destination { get; set; } = "";
}
