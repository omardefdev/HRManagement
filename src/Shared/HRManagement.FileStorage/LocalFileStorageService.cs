using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.FileStorage;

public sealed class LocalFileStorageService : IFileStorageService
{
    private readonly FileStorageOptions _options;
    private readonly ILogger<LocalFileStorageService> _logger;

    public LocalFileStorageService(IOptions<FileStorageOptions> options, ILogger<LocalFileStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    private string RootPath =>
        Path.IsPathRooted(_options.LocalRootPath)
            ? _options.LocalRootPath
            : Path.Combine(AppContext.BaseDirectory, _options.LocalRootPath);

    public Task EnsureBucketsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var bucket in FileStorageBuckets.All)
            Directory.CreateDirectory(Path.Combine(RootPath, bucket));
        return Task.CompletedTask;
    }

    public async Task<FileReference> UploadAsync(
        string bucket,
        string prefix,
        Stream content,
        string fileName,
        string contentType,
        long contentLength,
        CancellationToken cancellationToken = default)
    {
        var fileId = Guid.NewGuid();
        var objectKey = BuildObjectKey(prefix, fileId, fileName);
        var physicalPath = GetPhysicalPath(bucket, objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);

        await using (var fs = new FileStream(physicalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(fs, cancellationToken);
        }

        var bytes = await File.ReadAllBytesAsync(physicalPath, cancellationToken);
        var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        _logger.LogDebug("Stored local file at {Path}", physicalPath);

        return new FileReference(fileId, bucket, objectKey, contentType, contentLength, checksum);
    }

    public Task<FileReference> UploadFormFileAsync(
        string bucket,
        string prefix,
        IFormFile file,
        FileUploadConstraints constraints,
        CancellationToken cancellationToken = default)
    {
        var validation = FileUploadHelper.Validate(file, constraints);
        if (!validation.Success)
            throw new InvalidOperationException(validation.Error);

        return UploadAsync(bucket, prefix, file.OpenReadStream(), file.FileName, file.ContentType, file.Length, cancellationToken);
    }

    public async Task<FileReference> PutAsync(
        string bucket,
        string objectKey,
        Guid fileId,
        Stream content,
        string contentType,
        long contentLength,
        CancellationToken cancellationToken = default)
    {
        var physicalPath = GetPhysicalPath(bucket, objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
        await using (var fs = new FileStream(physicalPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(fs, cancellationToken);
        }

        var bytes = await File.ReadAllBytesAsync(physicalPath, cancellationToken);
        var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new FileReference(fileId, bucket, objectKey, contentType, bytes.LongLength, checksum);
    }

    public Task<string> GetPresignedDownloadUrlAsync(
        FileReference reference,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult($"/local-files/{reference.StorageKey}");

    public Task<string> GetPresignedUploadUrlAsync(
        string bucket,
        string objectKey,
        string contentType,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult($"/local-files/upload/{bucket}/{objectKey}");

    public Task<Stream> OpenReadAsync(FileReference reference, CancellationToken cancellationToken = default)
    {
        var path = GetPhysicalPath(reference.Bucket, reference.ObjectKey);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(FileReference reference, CancellationToken cancellationToken = default)
    {
        var path = GetPhysicalPath(reference.Bucket, reference.ObjectKey);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(FileReference reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(GetPhysicalPath(reference.Bucket, reference.ObjectKey)));

    public Task<IReadOnlyList<string>> ListObjectKeysAsync(string bucket, CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(RootPath, bucket);
        if (!Directory.Exists(root))
            return Task.FromResult<IReadOnlyList<string>>([]);

        var keys = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(keys);
    }

    private string GetPhysicalPath(string bucket, string objectKey) =>
        Path.Combine(RootPath, bucket, objectKey.Replace('/', Path.DirectorySeparatorChar));

    private static string BuildObjectKey(string prefix, Guid fileId, string fileName)
    {
        var safeName = FileUploadHelper.SanitizeFileName(fileName);
        var trimmedPrefix = prefix.Trim('/').Replace('\\', '/');
        return string.IsNullOrEmpty(trimmedPrefix)
            ? $"{fileId:N}/{safeName}"
            : $"{trimmedPrefix}/{fileId:N}/{safeName}";
    }
}
