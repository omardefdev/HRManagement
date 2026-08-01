namespace HRManagement.ServiceDefaults.Evidence;

public sealed class EvidenceRepositoryOptions
{
    public const string SectionName = "EvidenceRepository";

    /// <summary>Local root used when no <see cref="IEvidenceBlobStore"/> is registered.</summary>
    public string LocalRootPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "evidence-store");
}
