using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using File.Storage.Domain.Enums;
using File.Storage.Infrastructure.Data;
using File.Storage.Infrastructure.Services;
using HRManagement.FileStorage;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HRManagement.IntegrationTests;

/// <summary>FILE-002 malware scan, quarantine, CDR, download gating.</summary>
public class File002ScanningTests : IAsyncDisposable
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly SqliteConnection _connection;
    private readonly string _storageRoot;
    private readonly DbContextOptions<FileStorageDbContext> _options;

    public File002ScanningTests()
    {
        _storageRoot = Path.Combine(Path.GetTempPath(), "file002-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_storageRoot);
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<FileStorageDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        if (Directory.Exists(_storageRoot))
            Directory.Delete(_storageRoot, recursive: true);
    }

    [Fact]
    public async Task File_MimeConfusion_Rejected()
    {
        var storage = new ControllableStorage(_storageRoot);
        await using var db = CreateDb();
        var platform = CreatePlatform(db, storage, new EicarAwareScanner());

        // PDF extension with PNG bytes
        var png = Convert.FromHexString("89504E470D0A1A0A0000000D49484452");
        var form = CreateFormFile(png, "evil.pdf", "application/pdf");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            platform.UploadAsync(form, FileUploadConstraints.Document, null));
        Assert.Contains("magic", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task File_PendingScan_DownloadBlocked()
    {
        var storage = new ControllableStorage(_storageRoot);
        await using var db = CreateDb();
        var platform = CreatePlatform(db, storage, new EicarAwareScanner());

        var form = CreateFormFile(MinimalPdf(), "doc.pdf", "application/pdf");
        var record = await platform.UploadAsync(form, FileUploadConstraints.Document, null);
        Assert.Equal(FileScanStatus.PendingScan, record.ScanStatus);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            platform.OpenDownloadVerifiedAsync(record.Id));
    }

    [Fact]
    public async Task File_Eicar_Quarantined()
    {
        var storage = new ControllableStorage(_storageRoot);
        await using var db = CreateDb();
        var platform = CreatePlatform(db, storage, new EicarAwareScanner());

        var payload = Encoding.ASCII.GetBytes(EicarAwareScanner.EicarSignature);
        // EICAR is not a valid PDF — upload via direct quarantine seed + scan queue
        var fileId = await SeedQuarantineAsync(db, storage, "eicar.com", payload, "application/octet-stream");
        db.FileScanWorkItems.Add(new File.Storage.Domain.Entities.FileScanWorkItem
        {
            FileId = fileId,
            Status = FileScanWorkItemStatus.Pending,
            NextAttemptAt = DateTime.UtcNow.AddMinutes(-1),
            TenantId = TenantId
        });
        await db.SaveChangesAsync();

        await platform.ProcessScanQueueAsync();

        var record = await db.Files.AsNoTracking().SingleAsync(x => x.Id == fileId);
        Assert.Equal(FileScanStatus.Infected, record.ScanStatus);
        Assert.Equal(FileStorageBuckets.Quarantine, record.Bucket);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            platform.OpenDownloadVerifiedAsync(fileId));
    }

    [Fact]
    public async Task File_ScannerOutage_FailsClosed()
    {
        var storage = new ControllableStorage(_storageRoot);
        await using var db = CreateDb();
        var failing = new FailingScanner();
        var platform = CreatePlatform(db, storage, failing);

        var fileId = await SeedQuarantineAsync(db, storage, "doc.pdf", MinimalPdf(), "application/pdf");
        db.FileScanWorkItems.Add(new File.Storage.Domain.Entities.FileScanWorkItem
        {
            FileId = fileId,
            Status = FileScanWorkItemStatus.Pending,
            NextAttemptAt = DateTime.UtcNow.AddMinutes(-1),
            Attempt = 100,
            TenantId = TenantId
        });
        await db.SaveChangesAsync();

        await platform.ProcessScanQueueAsync();
        var record = await db.Files.AsNoTracking().SingleAsync(x => x.Id == fileId);
        Assert.Equal(FileScanStatus.ScanError, record.ScanStatus);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            platform.OpenDownloadVerifiedAsync(fileId));
    }

    [Fact]
    public async Task File_ZipBombOffice_Rejected()
    {
        await using var bomb = BuildZipBombDocx();
        var probe = await FileContentProbe.ProbeAsync(bomb, "bomb.docx", FileUploadConstraints.Document);
        Assert.False(probe.Success);
        Assert.Contains("تضخّم", probe.Error!);
    }

    [Fact]
    public async Task File_Clean_PromotedAfterScan()
    {
        var storage = new ControllableStorage(_storageRoot);
        await using var db = CreateDb();
        var platform = CreatePlatform(db, storage, new EicarAwareScanner());

        var form = CreateFormFile(MinimalPdf(), "clean.pdf", "application/pdf");
        var record = await platform.UploadAsync(form, FileUploadConstraints.Document, null);
        Assert.Equal(FileStorageBuckets.Quarantine, record.Bucket);

        foreach (var item in db.FileScanWorkItems)
            item.NextAttemptAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        await platform.ProcessScanQueueAsync();

        var updated = await db.Files.AsNoTracking().SingleAsync(x => x.Id == record.Id);
        Assert.Equal(FileScanStatus.Clean, updated.ScanStatus);
        Assert.Equal(FileStorageBuckets.FileStorage, updated.Bucket);
        Assert.NotNull(updated.ScannedAt);
        Assert.True(await storage.ExistsAsync(new FileReference(updated.Id, updated.Bucket, updated.ObjectKey, updated.MimeType, updated.Size, updated.Sha256)));

        var (stream, _, _) = await platform.OpenDownloadVerifiedAsync(updated.Id);
        await stream.DisposeAsync();
    }

    [Fact]
    public async Task File_Cdr_ImageReencoded()
    {
        var storage = new ControllableStorage(_storageRoot);
        await using var db = CreateDb();
        var platform = CreatePlatform(db, storage, new EicarAwareScanner());

        var jpeg = MinimalJpegWithExifApp1();
        var form = CreateFormFile(jpeg, "photo.jpg", "image/jpeg");
        var record = await platform.UploadAsync(form, FileUploadConstraints.Document, null);

        foreach (var item in db.FileScanWorkItems)
            item.NextAttemptAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        await platform.ProcessScanQueueAsync();

        var updated = await db.Files.AsNoTracking().SingleAsync(x => x.Id == record.Id);
        Assert.Equal(FileScanStatus.Clean, updated.ScanStatus);
        Assert.True(updated.CdrApplied);
        Assert.Equal("jpeg-strip-metadata", updated.CdrProfile);
    }

    [Fact]
    public async Task File_Infected_PresignBlocked()
    {
        var storage = new ControllableStorage(_storageRoot);
        await using var db = CreateDb();
        var platform = CreatePlatform(db, storage, new EicarAwareScanner());

        var fileId = await SeedQuarantineAsync(db, storage, "bad.pdf", MinimalPdf(), "application/pdf");
        var file = await db.Files.SingleAsync(x => x.Id == fileId);
        file.ScanStatus = FileScanStatus.Infected;
        file.ThreatName = "Test.Threat";
        await db.SaveChangesAsync();

        var record = await platform.GetAsync(fileId);
        Assert.NotNull(record);
        Assert.Throws<InvalidOperationException>(() => platform.EnsureDownloadAllowed(record!));
    }

    private FileStorageDbContext CreateDb() => new(_options, new FixedTenant(TenantId));

    private static FilePlatformService CreatePlatform(
        FileStorageDbContext db,
        IFileStorageService storage,
        IMalwareScanner scanner) =>
        new(db, storage, scanner, new ContentDisarmService(NullLogger<ContentDisarmService>.Instance));

    private async Task<Guid> SeedQuarantineAsync(
        FileStorageDbContext db,
        ControllableStorage storage,
        string name,
        byte[] content,
        string contentType)
    {
        await using var ms = new MemoryStream(content);
        var reference = await storage.UploadAsync(
            FileStorageBuckets.Quarantine, "2026/07", ms, name, contentType, content.Length);
        db.Files.Add(new File.Storage.Domain.Entities.FileRecord
        {
            Id = reference.FileId,
            TenantId = TenantId,
            OriginalName = name,
            Extension = Path.GetExtension(name),
            MimeType = contentType,
            DetectedContentType = contentType,
            Size = reference.SizeBytes,
            Sha256 = reference.Checksum,
            Bucket = reference.Bucket,
            ObjectKey = reference.ObjectKey,
            UploadedAt = DateTime.UtcNow,
            Status = FileRecordStatus.Pending,
            ScanStatus = FileScanStatus.PendingScan,
            ReferenceCount = 0
        });
        await db.SaveChangesAsync();
        return reference.FileId;
    }

    private static IFormFile CreateFormFile(byte[] bytes, string fileName, string contentType)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static byte[] MinimalPdf() =>
        Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");

    private static byte[] MinimalJpegWithExifApp1()
    {
        // SOI + APP1 (EXIF stub) + SOS stub + EOI
        var app1 = new byte[] { 0xFF, 0xE1, 0x00, 0x08, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0x00, 0x00 };
        var sos = new byte[] { 0xFF, 0xDA, 0x00, 0x02, 0x00, 0x00, 0xFF, 0xD9 };
        return new byte[] { 0xFF, 0xD8 }.Concat(app1).Concat(sos).ToArray();
    }

    private static MemoryStream BuildZipBombDocx()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var ct = zip.CreateEntry("[Content_Types].xml");
            using (var w = new StreamWriter(ct.Open()))
                w.Write("""<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"></Types>""");

            var word = zip.CreateEntry("word/document.xml");
            using (var w = new StreamWriter(word.Open()))
                w.Write("<w:document/>");

            // Highly compressible large entry
            var entry = zip.CreateEntry("word/big.bin", CompressionLevel.SmallestSize);
            using var s = entry.Open();
            var zeros = new byte[2_000_000];
            s.Write(zeros);
        }

        ms.Position = 0;
        return ms;
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }

    private sealed class FailingScanner : IMalwareScanner
    {
        public Task<MalwareScanOutcome> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MalwareScanOutcome(MalwareScanVerdict.Error, null, "Failing", "0", "simulated outage"));
    }

    private sealed class ControllableStorage : IFileStorageService
    {
        private readonly string _root;
        public ControllableStorage(string root) => _root = root;

        public Task EnsureBucketsAsync(CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.Combine(_root, FileStorageBuckets.FileStorage));
            Directory.CreateDirectory(Path.Combine(_root, FileStorageBuckets.Quarantine));
            return Task.CompletedTask;
        }

        public async Task<FileReference> UploadAsync(
            string bucket, string prefix, Stream content, string fileName, string contentType, long contentLength,
            CancellationToken cancellationToken = default)
        {
            var fileId = Guid.NewGuid();
            var objectKey = $"{prefix.Trim('/')}/{fileId:N}/{fileName}";
            return await PutAsync(bucket, objectKey, fileId, content, contentType, contentLength, cancellationToken);
        }

        public Task<FileReference> UploadFormFileAsync(string bucket, string prefix, IFormFile file, FileUploadConstraints constraints, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async Task<FileReference> PutAsync(
            string bucket, string objectKey, Guid fileId, Stream content, string contentType, long contentLength,
            CancellationToken cancellationToken = default)
        {
            await using var ms = new MemoryStream();
            await content.CopyToAsync(ms, cancellationToken);
            var bytes = ms.ToArray();
            var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            // Avoid writing known AV test payloads to disk (Windows Defender blocks EICAR).
            var ascii = Encoding.ASCII.GetString(bytes);
            if (!ascii.Contains(EicarAwareScanner.EicarSignature, StringComparison.Ordinal))
            {
                var path = Physical(bucket, objectKey);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await System.IO.File.WriteAllBytesAsync(path, bytes, cancellationToken);
            }
            else
            {
                _memory[Key(bucket, objectKey)] = bytes;
            }

            return new FileReference(fileId, bucket, objectKey, contentType, bytes.LongLength, checksum);
        }

        public Task<string> GetPresignedDownloadUrlAsync(FileReference reference, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
            => Task.FromResult("http://x");
        public Task<string> GetPresignedUploadUrlAsync(string bucket, string objectKey, string contentType, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
            => Task.FromResult("http://x");
        public Task<Stream> OpenReadAsync(FileReference reference, CancellationToken cancellationToken = default)
        {
            var key = Key(reference.Bucket, reference.ObjectKey);
            if (_memory.TryGetValue(key, out var bytes))
                return Task.FromResult<Stream>(new MemoryStream(bytes));
            Stream s = System.IO.File.OpenRead(Physical(reference.Bucket, reference.ObjectKey));
            return Task.FromResult(s);
        }
        public Task DeleteAsync(FileReference reference, CancellationToken cancellationToken = default)
        {
            var key = Key(reference.Bucket, reference.ObjectKey);
            _memory.Remove(key);
            var path = Physical(reference.Bucket, reference.ObjectKey);
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            return Task.CompletedTask;
        }
        public Task<bool> ExistsAsync(FileReference reference, CancellationToken cancellationToken = default)
        {
            var key = Key(reference.Bucket, reference.ObjectKey);
            if (_memory.ContainsKey(key)) return Task.FromResult(true);
            return Task.FromResult(System.IO.File.Exists(Physical(reference.Bucket, reference.ObjectKey)));
        }
        public Task<IReadOnlyList<string>> ListObjectKeysAsync(string bucket, CancellationToken cancellationToken = default)
        {
            var root = Path.Combine(_root, bucket);
            var keys = new List<string>();
            if (Directory.Exists(root))
            {
                keys.AddRange(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')));
            }

            keys.AddRange(_memory.Keys
                .Where(k => k.StartsWith(bucket + "/", StringComparison.Ordinal))
                .Select(k => k[(bucket.Length + 1)..]));
            return Task.FromResult<IReadOnlyList<string>>(keys);
        }

        private readonly Dictionary<string, byte[]> _memory = new(StringComparer.Ordinal);
        private static string Key(string bucket, string objectKey) => bucket + "/" + objectKey;
        private string Physical(string bucket, string objectKey) =>
            Path.Combine(_root, bucket, objectKey.Replace('/', Path.DirectorySeparatorChar));
    }
}
