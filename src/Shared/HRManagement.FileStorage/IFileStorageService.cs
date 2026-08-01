using Microsoft.AspNetCore.Http;

namespace HRManagement.FileStorage;

public interface IFileStorageService
{
    Task EnsureBucketsAsync(CancellationToken cancellationToken = default);

    Task<FileReference> UploadAsync(
        string bucket,
        string prefix,
        Stream content,
        string fileName,
        string contentType,
        long contentLength,
        CancellationToken cancellationToken = default);

    Task<FileReference> UploadFormFileAsync(
        string bucket,
        string prefix,
        IFormFile file,
        FileUploadConstraints constraints,
        CancellationToken cancellationToken = default);

    /// <summary>Store content at a known file id / object key (promotion from quarantine).</summary>
    Task<FileReference> PutAsync(
        string bucket,
        string objectKey,
        Guid fileId,
        Stream content,
        string contentType,
        long contentLength,
        CancellationToken cancellationToken = default);

    Task<string> GetPresignedDownloadUrlAsync(
        FileReference reference,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);

    Task<string> GetPresignedUploadUrlAsync(
        string bucket,
        string objectKey,
        string contentType,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(FileReference reference, CancellationToken cancellationToken = default);

    Task DeleteAsync(FileReference reference, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(FileReference reference, CancellationToken cancellationToken = default);

    /// <summary>Lists object keys under a bucket (for orphan reconcile).</summary>
    Task<IReadOnlyList<string>> ListObjectKeysAsync(string bucket, CancellationToken cancellationToken = default);
}
