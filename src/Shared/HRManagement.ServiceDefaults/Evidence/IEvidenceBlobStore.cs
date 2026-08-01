namespace HRManagement.ServiceDefaults.Evidence;

/// <summary>
/// Optional content backend for evidence bytes (local disk, or adapter over IFileStorageService / MinIO).
/// </summary>
public interface IEvidenceBlobStore
{
    /// <returns>Storage URI / path written.</returns>
    Task<string> StoreAsync(string artifactId, byte[] content, CancellationToken cancellationToken = default);
}

/// <summary>Writes evidence content under a local root path (tests / default Gateway).</summary>
public sealed class LocalEvidenceBlobStore(string rootPath) : IEvidenceBlobStore
{
    public async Task<string> StoreAsync(string artifactId, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentNullException.ThrowIfNull(content);

        Directory.CreateDirectory(rootPath);
        var safeName = artifactId.Replace('/', '_').Replace('\\', '_');
        var path = Path.Combine(rootPath, safeName + ".bin");
        await File.WriteAllBytesAsync(path, content, cancellationToken);
        return path;
    }
}
