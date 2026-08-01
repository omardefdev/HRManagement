using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace HRManagement.FileStorage;

public sealed class MinioFileStorageService : IFileStorageService
{
    private readonly IMinioClient _client;
    private readonly FileStorageOptions _options;
    private readonly ILogger<MinioFileStorageService> _logger;

    public MinioFileStorageService(
        IMinioClient client,
        IOptions<FileStorageOptions> options,
        ILogger<MinioFileStorageService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task EnsureBucketsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var bucket in FileStorageBuckets.All)
            await EnsureBucketAsync(bucket, cancellationToken);
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
        await EnsureBucketAsync(bucket, cancellationToken);

        var fileId = Guid.NewGuid();
        var objectKey = BuildObjectKey(prefix, fileId, fileName);
        string? checksum = null;

        await using (var hashStream = new MemoryStream())
        {
            await content.CopyToAsync(hashStream, cancellationToken);
            hashStream.Position = 0;
            checksum = Convert.ToHexString(SHA256.HashData(hashStream.ToArray())).ToLowerInvariant();
            hashStream.Position = 0;

            await _client.PutObjectAsync(
                new PutObjectArgs()
                    .WithBucket(bucket)
                    .WithObject(objectKey)
                    .WithStreamData(hashStream)
                    .WithObjectSize(hashStream.Length)
                    .WithContentType(contentType),
                cancellationToken);
        }

        return new FileReference(fileId, bucket, objectKey, contentType, contentLength, checksum);
    }

    public async Task<FileReference> UploadFormFileAsync(
        string bucket,
        string prefix,
        IFormFile file,
        FileUploadConstraints constraints,
        CancellationToken cancellationToken = default)
    {
        var validation = FileUploadHelper.Validate(file, constraints);
        if (!validation.Success)
            throw new InvalidOperationException(validation.Error);

        await using var stream = file.OpenReadStream();
        return await UploadAsync(
            bucket,
            prefix,
            stream,
            file.FileName,
            file.ContentType,
            file.Length,
            cancellationToken);
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
        await EnsureBucketAsync(bucket, cancellationToken);
        await using var hashStream = new MemoryStream();
        await content.CopyToAsync(hashStream, cancellationToken);
        hashStream.Position = 0;
        var checksum = Convert.ToHexString(SHA256.HashData(hashStream.ToArray())).ToLowerInvariant();
        hashStream.Position = 0;

        await _client.PutObjectAsync(
            new PutObjectArgs()
                .WithBucket(bucket)
                .WithObject(objectKey)
                .WithStreamData(hashStream)
                .WithObjectSize(hashStream.Length)
                .WithContentType(contentType),
            cancellationToken);

        return new FileReference(fileId, bucket, objectKey, contentType, hashStream.Length, checksum);
    }

    public Task<string> GetPresignedDownloadUrlAsync(
        FileReference reference,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        var seconds = (int)(expiry ?? TimeSpan.FromMinutes(_options.PresignedUrlExpiryMinutes)).TotalSeconds;
        return _client.PresignedGetObjectAsync(
            new PresignedGetObjectArgs()
                .WithBucket(reference.Bucket)
                .WithObject(reference.ObjectKey)
                .WithExpiry(seconds));
    }

    public Task<string> GetPresignedUploadUrlAsync(
        string bucket,
        string objectKey,
        string contentType,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        var seconds = (int)(expiry ?? TimeSpan.FromMinutes(_options.PresignedUrlExpiryMinutes)).TotalSeconds;
        return _client.PresignedPutObjectAsync(
            new PresignedPutObjectArgs()
                .WithBucket(bucket)
                .WithObject(objectKey)
                .WithExpiry(seconds));
    }

    public async Task<Stream> OpenReadAsync(FileReference reference, CancellationToken cancellationToken = default)
    {
        var ms = new MemoryStream();
        await _client.GetObjectAsync(
            new GetObjectArgs()
                .WithBucket(reference.Bucket)
                .WithObject(reference.ObjectKey)
                .WithCallbackStream(stream => stream.CopyTo(ms)),
            cancellationToken);
        ms.Position = 0;
        return ms;
    }

    public Task DeleteAsync(FileReference reference, CancellationToken cancellationToken = default) =>
        _client.RemoveObjectAsync(
            new RemoveObjectArgs()
                .WithBucket(reference.Bucket)
                .WithObject(reference.ObjectKey),
            cancellationToken);

    public async Task<bool> ExistsAsync(FileReference reference, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.StatObjectAsync(
                new StatObjectArgs()
                    .WithBucket(reference.Bucket)
                    .WithObject(reference.ObjectKey),
                cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Object not found: {Bucket}/{Key}", reference.Bucket, reference.ObjectKey);
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> ListObjectKeysAsync(string bucket, CancellationToken cancellationToken = default)
    {
        var keys = new List<string>();
        await foreach (var item in _client.ListObjectsEnumAsync(
            new ListObjectsArgs().WithBucket(bucket).WithRecursive(true),
            cancellationToken))
        {
            if (!string.IsNullOrEmpty(item.Key) && !item.Key.EndsWith('/'))
                keys.Add(item.Key);
        }

        return keys;
    }

    private async Task EnsureBucketAsync(string bucket, CancellationToken cancellationToken)
    {
        var exists = await _client.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(bucket),
            cancellationToken);
        if (!exists)
        {
            await _client.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(bucket),
                cancellationToken);
            _logger.LogInformation("Created MinIO bucket {Bucket}", bucket);
        }
    }

    private static string BuildObjectKey(string prefix, Guid fileId, string fileName)
    {
        var safeName = FileUploadHelper.SanitizeFileName(fileName);
        var trimmedPrefix = prefix.Trim('/').Replace('\\', '/');
        return string.IsNullOrEmpty(trimmedPrefix)
            ? $"{fileId:N}/{safeName}"
            : $"{trimmedPrefix}/{fileId:N}/{safeName}";
    }
}
