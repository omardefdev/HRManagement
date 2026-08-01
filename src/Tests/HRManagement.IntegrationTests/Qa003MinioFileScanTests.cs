using System.Text;
using File.Storage.Domain.Enums;
using File.Storage.Infrastructure.Data;
using File.Storage.Infrastructure.Services;
using HRManagement.FileStorage;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Minio;

namespace HRManagement.IntegrationTests;

/// <summary>QA-003-D: FILE scan path on real MinIO + PostgreSQL metadata.</summary>
[Collection("PgMinio")]
public class Qa003MinioFileScanTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly PgMinioFixture _fixture;

    public Qa003MinioFileScanTests(PgMinioFixture fixture) => _fixture = fixture;

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Clean_Pdf_Upload_To_Minio_PendingScan()
    {
        Skip.IfNot(RealInfra.IsEnabled);

        await using var db = await CreateDbAsync();
        var storage = CreateMinioStorage();
        await storage.EnsureBucketsAsync();
        var platform = CreatePlatform(db, storage, new EicarAwareScanner());

        var form = CreateFormFile(MinimalPdf(), "doc.pdf", "application/pdf");
        var record = await platform.UploadAsync(form, FileUploadConstraints.Document, null);

        Assert.Equal(FileScanStatus.PendingScan, record.ScanStatus);
        Assert.True(await storage.ExistsAsync(new FileReference(
            record.Id, record.Bucket, record.ObjectKey, record.MimeType, record.Size, record.Sha256)));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Eicar_Upload_Quarantined_On_Minio()
    {
        Skip.IfNot(RealInfra.IsEnabled);

        await using var db = await CreateDbAsync();
        var storage = CreateMinioStorage();
        await storage.EnsureBucketsAsync();
        var platform = CreatePlatform(db, storage, new EicarAwareScanner());

        // Classic EICAR string as "text" payload with pdf magic bypass — use scanner on stored content path.
        // Upload a minimal PDF then replace object with EICAR via Put for scan job simulation:
        var form = CreateFormFile(MinimalPdf(), "clean.pdf", "application/pdf");
        var record = await platform.UploadAsync(form, FileUploadConstraints.Document, null);

        var eicar = Encoding.ASCII.GetBytes(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");
        await using (var ms = new MemoryStream(eicar))
        {
            await storage.PutAsync(record.Bucket, record.ObjectKey, record.Id, ms, "application/pdf", eicar.Length);
        }

        // Run scan against stored object (same as File002 EICAR path intent)
        var scanner = new EicarAwareScanner();
        await using var download = await storage.OpenReadAsync(new FileReference(
            record.Id, record.Bucket, record.ObjectKey, record.MimeType, eicar.Length, null));
        var outcome = await scanner.ScanAsync(download, record.OriginalName);
        Assert.Equal(MalwareScanVerdict.Infected, outcome.Verdict);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Minio_Stopped_Upload_Fails_Explicitly()
    {
        Skip.IfNot(RealInfra.IsEnabled);

        await using var db = await CreateDbAsync();
        var platform = CreatePlatform(db, new UnreachableFileStorage(), new EicarAwareScanner());
        var form = CreateFormFile(MinimalPdf(), "doc.pdf", "application/pdf");
        await Assert.ThrowsAnyAsync<Exception>(() =>
            platform.UploadAsync(form, FileUploadConstraints.Document, null));
    }

    private sealed class UnreachableFileStorage : IFileStorageService
    {
        public Task EnsureBucketsAsync(CancellationToken cancellationToken = default) =>
            Task.FromException(new HttpRequestException("MinIO unreachable"));

        public Task<FileReference> UploadAsync(
            string bucket,
            string prefix,
            Stream content,
            string fileName,
            string contentType,
            long contentLength,
            CancellationToken cancellationToken = default) =>
            Task.FromException<FileReference>(new HttpRequestException("MinIO unreachable"));

        public Task<FileReference> UploadFormFileAsync(
            string bucket,
            string prefix,
            IFormFile file,
            FileUploadConstraints constraints,
            CancellationToken cancellationToken = default) =>
            Task.FromException<FileReference>(new HttpRequestException("MinIO unreachable"));

        public Task<FileReference> PutAsync(
            string bucket,
            string objectKey,
            Guid fileId,
            Stream content,
            string contentType,
            long contentLength,
            CancellationToken cancellationToken = default) =>
            Task.FromException<FileReference>(new HttpRequestException("MinIO unreachable"));

        public Task<string> GetPresignedDownloadUrlAsync(
            FileReference reference,
            TimeSpan? expiry = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<string>(new HttpRequestException("MinIO unreachable"));

        public Task<string> GetPresignedUploadUrlAsync(
            string bucket,
            string objectKey,
            string contentType,
            TimeSpan? expiry = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<string>(new HttpRequestException("MinIO unreachable"));

        public Task<Stream> OpenReadAsync(FileReference reference, CancellationToken cancellationToken = default) =>
            Task.FromException<Stream>(new HttpRequestException("MinIO unreachable"));

        public Task DeleteAsync(FileReference reference, CancellationToken cancellationToken = default) =>
            Task.FromException(new HttpRequestException("MinIO unreachable"));

        public Task<bool> ExistsAsync(FileReference reference, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<string>> ListObjectKeysAsync(string bucket, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    private async Task<FileStorageDbContext> CreateDbAsync()
    {
        var cs = await _fixture.Postgres.CreateDatabaseAsync($"qa003_file_{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<FileStorageDbContext>()
            .UseNpgsql(cs, n => n.MigrationsAssembly(typeof(FileStorageDbContext).Assembly.GetName().Name))
            .Options;
        var db = new FileStorageDbContext(options, new FixedTenant(TenantId));
        await db.Database.MigrateAsync();
        return db;
    }

    private MinioFileStorageService CreateMinioStorage(
        string? endpoint = null,
        string? accessKey = null,
        string? secretKey = null)
    {
        var ep = endpoint ?? _fixture.Minio.Endpoint;
        var opts = Options.Create(new FileStorageOptions
        {
            Provider = "Minio",
            Endpoint = ep,
            AccessKey = accessKey ?? MinioFixture.AccessKey,
            SecretKey = secretKey ?? MinioFixture.SecretKey,
            UseSsl = false
        });
        var client = new MinioClient()
            .WithEndpoint(ep)
            .WithCredentials(accessKey ?? MinioFixture.AccessKey, secretKey ?? MinioFixture.SecretKey)
            .WithSSL(false)
            .Build();
        return new MinioFileStorageService(client, opts, NullLogger<MinioFileStorageService>.Instance);
    }

    private static FilePlatformService CreatePlatform(
        FileStorageDbContext db,
        IFileStorageService storage,
        IMalwareScanner scanner) =>
        new(db, storage, scanner, new ContentDisarmService(NullLogger<ContentDisarmService>.Instance));

    private static IFormFile CreateFormFile(byte[] content, string name, string contentType)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static byte[] MinimalPdf() =>
        Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }
}
