using System.Security.Cryptography;
using File.Storage.Domain.Entities;
using File.Storage.Domain.Enums;
using File.Storage.Infrastructure.Data;
using File.Storage.Infrastructure.Services;
using HRManagement.FileStorage;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.IntegrationTests;

/// <summary>FILE-001 file lifecycle: two-phase purge, hold, reconcile, checksum, outbox.</summary>
public class File001LifecycleTests : IAsyncDisposable
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly SqliteConnection _connection;
    private readonly string _storageRoot;
    private readonly DbContextOptions<FileStorageDbContext> _options;

    public File001LifecycleTests()
    {
        _storageRoot = Path.Combine(Path.GetTempPath(), "file001-" + Guid.NewGuid().ToString("N"));
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
    public async Task File_Delete_WithRefs_Rejected()
    {
        var storage = new ControllableStorage(_storageRoot);
        await using var db = CreateDb();
        var platform = CreatePlatform(db, storage);
        var fileId = await SeedFileAsync(db, storage, "doc.pdf", "hello");

        await platform.RegisterReferenceAsync(fileId, "hr-core:EmployeeDocument:abc:FileId");
        var deleted = await platform.TryDeleteWhenUnreferencedAsync(fileId, null);
        Assert.False(deleted);

        var record = await db.Files.AsNoTracking().SingleAsync(x => x.Id == fileId);
        Assert.False(record.IsDeleted);
        Assert.Equal(FileRecordStatus.Active, record.Status);
        Assert.True(await storage.ExistsAsync(ToRef(record)));
    }

    [Fact]
    public async Task File_PendingPurge_NoImmediateObjectDelete()
    {
        var storage = new ControllableStorage(_storageRoot);
        await using var db = CreateDb();
        var platform = CreatePlatform(db, storage);
        var fileId = await SeedFileAsync(db, storage, "doc.pdf", "hello");

        var deleted = await platform.TryDeleteWhenUnreferencedAsync(fileId, null);
        Assert.True(deleted);

        var record = await db.Files.AsNoTracking().SingleAsync(x => x.Id == fileId);
        Assert.Equal(FileRecordStatus.PendingPurge, record.Status);
        Assert.True(record.IsDeleted);
        Assert.True(await storage.ExistsAsync(ToRef(record)));
        Assert.Equal(1, await db.FilePurgeWorkItems.CountAsync(x => x.FileId == fileId && x.Status == FilePurgeWorkItemStatus.Pending));
    }

    [Fact]
    public async Task File_MinioDeleteFail_RetriesThenSucceeds()
    {
        var storage = new ControllableStorage(_storageRoot) { FailDeletesRemaining = 2 };
        await using var db = CreateDb();
        var platform = CreatePlatform(db, storage);
        var fileId = await SeedFileAsync(db, storage, "doc.pdf", "hello");

        Assert.True(await platform.TryDeleteWhenUnreferencedAsync(fileId, null));

        Assert.Equal(0, await platform.ProcessPurgeQueueAsync());
        await ForcePurgeDueAsync(db);
        Assert.Equal(0, await platform.ProcessPurgeQueueAsync());
        await ForcePurgeDueAsync(db);
        Assert.Equal(1, await platform.ProcessPurgeQueueAsync());

        var record = await db.Files.AsNoTracking().SingleAsync(x => x.Id == fileId);
        Assert.Equal(FileRecordStatus.Deleted, record.Status);
        Assert.NotNull(record.ObjectPurgedAt);
        Assert.False(await storage.ExistsAsync(ToRef(record)));
        Assert.Equal(FilePurgeWorkItemStatus.Succeeded,
            await db.FilePurgeWorkItems.Where(x => x.FileId == fileId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task File_RefCount_Reconcile_RepairsDrift()
    {
        var storage = new ControllableStorage(_storageRoot);
        await using var db = CreateDb();
        var platform = CreatePlatform(db, storage);
        var fileId = await SeedFileAsync(db, storage, "doc.pdf", "hello");
        await platform.RegisterReferenceAsync(fileId, "holder-a");
        await platform.RegisterReferenceAsync(fileId, "holder-b");

        await db.Files.Where(x => x.Id == fileId)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.ReferenceCount, 99));

        var repaired = await platform.ReconcileReferenceCountsAsync();
        Assert.Equal(1, repaired);

        var count = await db.Files.Where(x => x.Id == fileId).Select(x => x.ReferenceCount).SingleAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task File_LegalHold_BlocksPurge()
    {
        var storage = new ControllableStorage(_storageRoot);
        await using var db = CreateDb();
        var platform = CreatePlatform(db, storage);
        var fileId = await SeedFileAsync(db, storage, "doc.pdf", "hello");

        var file = await db.Files.SingleAsync(x => x.Id == fileId);
        file.LegalHold = true;
        await db.SaveChangesAsync();

        Assert.False(await platform.TryDeleteWhenUnreferencedAsync(fileId, null));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            platform.ForceDeleteAsync(fileId, null, "audit reason"));

        Assert.True(await storage.ExistsAsync(ToRef(file)));
    }

    [Fact]
    public async Task File_OrphanScan_ReportsAndClears()
    {
        var storage = new ControllableStorage(_storageRoot);
        await using var db = CreateDb();
        var platform = CreatePlatform(db, storage);
        var fileId = await SeedFileAsync(db, storage, "doc.pdf", "hello");
        var record = await db.Files.AsNoTracking().SingleAsync(x => x.Id == fileId);

        // Object without DB
        var orphanDir = Path.Combine(_storageRoot, FileStorageBuckets.FileStorage, "orphan");
        Directory.CreateDirectory(orphanDir);
        await System.IO.File.WriteAllTextAsync(Path.Combine(orphanDir, "extra.pdf"), "x");

        // DB without object
        await storage.DeleteAsync(ToRef(record));

        var scan = await platform.ScanOrphansAsync(persistExceptions: true);
        Assert.True(scan.DbWithoutObject >= 1);
        Assert.True(scan.ObjectWithoutDb >= 1);
        Assert.True(scan.UnresolvedExceptions >= 1);

        var ex = await db.FileReconcileExceptions.FirstAsync(x => x.Kind == "ObjectWithoutDb");
        await platform.ApproveReconcileExceptionAsync(ex.Id, Guid.NewGuid());

        var scan2 = await platform.ScanOrphansAsync(persistExceptions: false);
        Assert.Contains(scan2.Exceptions, e => e.Kind == "ObjectWithoutDb" && e.Approved);
    }

    [Fact]
    public async Task File_ChecksumMismatch_FailsClosed()
    {
        var storage = new ControllableStorage(_storageRoot);
        await using var db = CreateDb();
        var platform = CreatePlatform(db, storage);
        var fileId = await SeedFileAsync(db, storage, "doc.pdf", "hello");

        var record = await db.Files.SingleAsync(x => x.Id == fileId);
        record.Sha256 = Convert.ToHexString(SHA256.HashData("other"u8.ToArray())).ToLowerInvariant();
        record.Status = FileRecordStatus.Active;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            platform.OpenDownloadVerifiedAsync(fileId));
    }

    [Fact]
    public async Task File_ReferenceOutbox_SurvivesTransientFailure()
    {
        await using var holderConn = new SqliteConnection("Data Source=:memory:");
        await holderConn.OpenAsync();
        var holderOptions = new DbContextOptionsBuilder<FileStorageDbContext>()
            .UseSqlite(holderConn)
            .Options;
        await using (var setup = new FileStorageDbContext(holderOptions, new FixedTenant(TenantId)))
            await setup.Database.EnsureCreatedAsync();

        var delivery = new FlakyReferenceClient { FailuresRemaining = 1 };
        await using var db = new FileStorageDbContext(holderOptions, new FixedTenant(TenantId));
        var outbox = new EfFileReferenceOutbox(db, new FixedTenant(TenantId));
        var client = new OutboxFileReferenceClient(outbox);

        var fileId = Guid.NewGuid();
        await EntityFileReferenceHelper.ApplyFileIdChangeAsync(
            client, "hr-core", "EmployeeDocument", Guid.NewGuid(), "FileId", null, fileId);
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.FileReferenceOutbox.CountAsync(x => x.Status == FileReferenceOutboxStatus.Pending));

        // First delivery fails
        var processed = await FileReferenceOutboxDispatcherJob<FileStorageDbContext>.ProcessBatchAsync(db, delivery, CancellationToken.None);
        Assert.Equal(0, processed);
        Assert.Equal(1, await db.FileReferenceOutbox.CountAsync(x => x.Status == FileReferenceOutboxStatus.Pending));

        // Force due
        foreach (var msg in db.FileReferenceOutbox)
            msg.NextAttemptAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        processed = await FileReferenceOutboxDispatcherJob<FileStorageDbContext>.ProcessBatchAsync(db, delivery, CancellationToken.None);
        Assert.Equal(1, processed);
        Assert.Equal(1, delivery.RegisterCalls);
        Assert.Equal(FileReferenceOutboxStatus.Succeeded,
            await db.FileReferenceOutbox.Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task File_ForceDelete_RequiresReason_AndRejectsLiveRefs()
    {
        var storage = new ControllableStorage(_storageRoot);
        await using var db = CreateDb();
        var platform = CreatePlatform(db, storage);
        var fileId = await SeedFileAsync(db, storage, "doc.pdf", "hello");
        await platform.RegisterReferenceAsync(fileId, "holder");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            platform.ForceDeleteAsync(fileId, null, "  "));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            platform.ForceDeleteAsync(fileId, null, "need cleanup"));

        await platform.ForceDeleteAsync(fileId, null, "approved override", overrideLiveRefs: true);
        var record = await db.Files.AsNoTracking().SingleAsync(x => x.Id == fileId);
        Assert.Equal(FileRecordStatus.PendingPurge, record.Status);
        Assert.Equal("approved override", record.ForceDeleteReason);
        Assert.Equal(0, await db.FileReferences.CountAsync(x => x.FileId == fileId));
    }

    private static async Task ForcePurgeDueAsync(FileStorageDbContext db)
    {
        foreach (var item in db.FilePurgeWorkItems.Where(x => x.Status == FilePurgeWorkItemStatus.Pending))
            item.NextAttemptAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
    }

    private static FilePlatformService CreatePlatform(FileStorageDbContext db, IFileStorageService storage) =>
        new(db, storage, new EicarAwareScanner(), new ContentDisarmService(Microsoft.Extensions.Logging.Abstractions.NullLogger<ContentDisarmService>.Instance));

    private FileStorageDbContext CreateDb() => new(_options, new FixedTenant(TenantId));

    private async Task<Guid> SeedFileAsync(FileStorageDbContext db, ControllableStorage storage, string name, string content)
    {
        await using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        var reference = await storage.UploadAsync(
            FileStorageBuckets.FileStorage, "2026/07", ms, name, "application/pdf", ms.Length);

        db.Files.Add(new FileRecord
        {
            Id = reference.FileId,
            TenantId = TenantId,
            OriginalName = name,
            Extension = Path.GetExtension(name),
            MimeType = reference.ContentType,
            Size = reference.SizeBytes,
            Sha256 = reference.Checksum,
            Bucket = reference.Bucket,
            ObjectKey = reference.ObjectKey,
            UploadedAt = DateTime.UtcNow,
            Status = FileRecordStatus.Pending,
            ScanStatus = FileScanStatus.Clean,
            ReferenceCount = 0
        });
        await db.SaveChangesAsync();
        return reference.FileId;
    }

    private static FileReference ToRef(FileRecord r) =>
        new(r.Id, r.Bucket, r.ObjectKey, r.MimeType, r.Size, r.Sha256);

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }

    private sealed class FlakyReferenceClient : ITenantFileReferenceDelivery
    {
        public int FailuresRemaining { get; set; }
        public int RegisterCalls { get; private set; }

        public Task RegisterReferenceForTenantAsync(Guid tenantId, Guid fileId, string holderKey, CancellationToken cancellationToken = default)
        {
            if (FailuresRemaining-- > 0)
                throw new HttpRequestException("transient");
            RegisterCalls++;
            return Task.CompletedTask;
        }

        public Task ReleaseReferenceForTenantAsync(Guid tenantId, Guid fileId, string holderKey, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ControllableStorage : IFileStorageService
    {
        private readonly string _root;
        public int FailDeletesRemaining { get; set; }

        public ControllableStorage(string root)
        {
            _root = root;
            Directory.CreateDirectory(Path.Combine(_root, FileStorageBuckets.FileStorage));
        }

        public Task EnsureBucketsAsync(CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.Combine(_root, FileStorageBuckets.FileStorage));
            return Task.CompletedTask;
        }

        public async Task<FileReference> UploadAsync(
            string bucket, string prefix, Stream content, string fileName, string contentType, long contentLength,
            CancellationToken cancellationToken = default)
        {
            var fileId = Guid.NewGuid();
            var objectKey = $"{prefix.Trim('/')}/{fileId:N}/{fileName}";
            var path = Physical(bucket, objectKey);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var bytes = new byte[contentLength > 0 ? contentLength : 0];
            await using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await content.CopyToAsync(fs, cancellationToken);
            }

            bytes = await System.IO.File.ReadAllBytesAsync(path, cancellationToken);
            var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return new FileReference(fileId, bucket, objectKey, contentType, bytes.LongLength, checksum);
        }

        public Task<FileReference> UploadFormFileAsync(string bucket, string prefix, IFormFile file, FileUploadConstraints constraints, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async Task<FileReference> PutAsync(
            string bucket, string objectKey, Guid fileId, Stream content, string contentType, long contentLength,
            CancellationToken cancellationToken = default)
        {
            var path = Physical(bucket, objectKey);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                await content.CopyToAsync(fs, cancellationToken);
            var bytes = await System.IO.File.ReadAllBytesAsync(path, cancellationToken);
            var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return new FileReference(fileId, bucket, objectKey, contentType, bytes.LongLength, checksum);
        }

        public Task<string> GetPresignedDownloadUrlAsync(FileReference reference, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
            => Task.FromResult("http://x");

        public Task<string> GetPresignedUploadUrlAsync(string bucket, string objectKey, string contentType, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
            => Task.FromResult("http://x");

        public Task<Stream> OpenReadAsync(FileReference reference, CancellationToken cancellationToken = default)
        {
            Stream s = System.IO.File.OpenRead(Physical(reference.Bucket, reference.ObjectKey));
            return Task.FromResult(s);
        }

        public Task DeleteAsync(FileReference reference, CancellationToken cancellationToken = default)
        {
            if (FailDeletesRemaining > 0)
            {
                FailDeletesRemaining--;
                throw new IOException("simulated storage delete failure");
            }

            var path = Physical(reference.Bucket, reference.ObjectKey);
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(FileReference reference, CancellationToken cancellationToken = default) =>
            Task.FromResult(System.IO.File.Exists(Physical(reference.Bucket, reference.ObjectKey)));

        public Task<IReadOnlyList<string>> ListObjectKeysAsync(string bucket, CancellationToken cancellationToken = default)
        {
            var root = Path.Combine(_root, bucket);
            if (!Directory.Exists(root))
                return Task.FromResult<IReadOnlyList<string>>([]);
            var keys = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
                .ToList();
            return Task.FromResult<IReadOnlyList<string>>(keys);
        }

        private string Physical(string bucket, string objectKey) =>
            Path.Combine(_root, bucket, objectKey.Replace('/', Path.DirectorySeparatorChar));
    }
}
