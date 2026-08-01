using HRManagement.SharedKernel.Evidence;

namespace HRManagement.ServiceDefaults.Evidence;

public interface IEvidenceRepositoryService
{
    Task<EvidenceArtifact> IngestAsync(EvidenceIngestRequest request, CancellationToken cancellationToken = default);

    Task<EvidenceArtifact?> GetAsync(string artifactId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvidenceArtifact>> ListExpiredAsync(CancellationToken cancellationToken = default);

    Task<EvidencePackExport> ExportPackAsync(
        IEnumerable<string> cardIds,
        string exportedBy,
        string destination = "local",
        CancellationToken cancellationToken = default);
}

public sealed record EvidenceIngestRequest(
    string ArtifactId,
    string CardId,
    string ReleaseDigest,
    string SourceType,
    byte[] Content,
    string? ControlId = null,
    string? Signature = null,
    DateTime? ExpiresAtUtc = null,
    string Classification = "Internal");
