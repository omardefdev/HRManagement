using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HRManagement.SharedKernel.Evidence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.Evidence;

/// <summary>
/// EVD-003 central immutable artifact store. Append-only; no update/delete APIs.
/// </summary>
public sealed class EvidenceRepositoryService(
    DbContext db,
    IOptions<EvidenceRepositoryOptions> options,
    IEvidenceBlobStore? blobStore = null) : IEvidenceRepositoryService
{
    public async Task<EvidenceArtifact> IngestAsync(
        EvidenceIngestRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CardId);
        ArgumentNullException.ThrowIfNull(request.Content);

        var existing = await db.Set<EvidenceArtifact>().AsNoTracking()
            .AnyAsync(a => a.ArtifactId == request.ArtifactId, cancellationToken);
        if (existing)
            throw new InvalidOperationException($"Evidence artifact '{request.ArtifactId}' already exists (append-only).");

        var hash = Convert.ToHexString(SHA256.HashData(request.Content)).ToLowerInvariant();
        var store = blobStore ?? new LocalEvidenceBlobStore(options.Value.LocalRootPath);
        var uri = await store.StoreAsync(request.ArtifactId, request.Content, cancellationToken);

        var artifact = new EvidenceArtifact
        {
            ArtifactId = request.ArtifactId.Trim(),
            CardId = request.CardId.Trim(),
            ControlId = request.ControlId,
            ReleaseDigest = request.ReleaseDigest ?? "",
            SourceType = request.SourceType ?? "",
            ContentHash = hash,
            Signature = request.Signature,
            TrustedTimestampUtc = DateTime.UtcNow,
            ExpiresAtUtc = request.ExpiresAtUtc,
            Classification = string.IsNullOrWhiteSpace(request.Classification) ? "Internal" : request.Classification,
            StorageUri = uri
        };

        db.Set<EvidenceArtifact>().Add(artifact);
        await db.SaveChangesAsync(cancellationToken);
        return artifact;
    }

    public async Task<EvidenceArtifact?> GetAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(artifactId))
            return null;

        return await db.Set<EvidenceArtifact>().AsNoTracking()
            .FirstOrDefaultAsync(a => a.ArtifactId == artifactId, cancellationToken);
    }

    public async Task<IReadOnlyList<EvidenceArtifact>> ListExpiredAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var now = DateTime.UtcNow;
        return await db.Set<EvidenceArtifact>().AsNoTracking()
            .Where(a => a.ExpiresAtUtc != null && a.ExpiresAtUtc <= now)
            .OrderBy(a => a.ExpiresAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<EvidencePackExport> ExportPackAsync(
        IEnumerable<string> cardIds,
        string exportedBy,
        string destination = "local",
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var cards = cardIds
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (cards.Length == 0)
            throw new ArgumentException("At least one card id is required.", nameof(cardIds));

        var artifacts = await db.Set<EvidenceArtifact>().AsNoTracking()
            .Where(a => cards.Contains(a.CardId))
            .OrderBy(a => a.ArtifactId)
            .ToListAsync(cancellationToken);

        var artifactIds = artifacts.Select(a => a.ArtifactId).ToList();
        var packHash = ComputePackHash(artifacts.Select(a => a.ContentHash));

        var pack = new EvidencePackExport
        {
            PackId = $"pack-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..40],
            ExportedAtUtc = DateTime.UtcNow,
            ExportedBy = string.IsNullOrWhiteSpace(exportedBy) ? "system" : exportedBy.Trim(),
            ArtifactIdsJson = JsonSerializer.Serialize(artifactIds),
            PackHash = packHash,
            Destination = destination
        };

        db.Set<EvidencePackExport>().Add(pack);
        await db.SaveChangesAsync(cancellationToken);
        return pack;
    }

    public static string ComputePackHash(IEnumerable<string> contentHashes)
    {
        var joined = string.Join('|', contentHashes
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h.Trim().ToLowerInvariant())
            .OrderBy(h => h, StringComparer.Ordinal));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private void EnsureConfigured()
    {
        if (db.Model.FindEntityType(typeof(EvidenceArtifact)) is null)
            throw new InvalidOperationException("DbContext is not configured with EvidenceArtifacts (EVD-003).");
    }
}
