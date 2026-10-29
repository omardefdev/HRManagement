using File.Storage.Domain.Entities;
using File.Storage.Domain.Enums;
using File.Storage.Infrastructure.Data;
using File.Storage.Infrastructure.Jobs;
using File.Storage.Infrastructure.Services;
using HRManagement.FileStorage;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>
/// File.Storage GC across tenants: the GC job has no request tenant (EffectiveTenantId = Guid.Empty), so before the fix
/// it saw no tenant-owned file. With <c>allTenants</c> it marks and purges each tenant's orphan Pending uploads and
/// released Active files in that tenant; without it (the default until HR.Core delivers references per tenant) nothing
/// tenant-owned is touched. Fresh, referenced, held and retained files are never collected.
/// </summary>
public class FileGarbageCollectionTenantTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("9c000000-0000-4000-8000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("9c000000-0000-4000-8000-00000000000b");
    private static readonly DateTime Old = DateTime.UtcNow.AddHours(-48);

    private readonly SqliteConnection _connection;
    private readonly Mock<IFileStorageService> _storage = new();

    public FileGarbageCollectionTenantTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        using var db = Context(null);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Without_allTenants_the_tenantless_job_collects_nothing_tenant_owned()
    {
        var seed = await SeedAsync();

        await using var db = Context(null);
        var platform = Platform(db);
        Assert.Equal(0, await platform.CollectGarbageAsync());
        Assert.Equal(0, await platform.CollectGarbageAsync(allTenants: false));
        Assert.Equal(0, await platform.ProcessPurgeQueueAsync(allTenants: false));

        await using var check = Context(null);
        var statuses = await AllFiles(check).ToDictionaryAsync(f => f.Id, f => f.Status);
        Assert.Equal(FileRecordStatus.Pending, statuses[seed.OrphanA]);
        Assert.Equal(FileRecordStatus.Pending, statuses[seed.OrphanB]);
        Assert.Equal(FileRecordStatus.Active, statuses[seed.ReleasedB]);
        Assert.False(await AllPurgeItems(check).AnyAsync());
    }

    [Fact]
    public async Task With_allTenants_each_tenants_orphans_are_marked_and_purged_in_their_own_tenant()
    {
        var seed = await SeedAsync();

        await using (var db = Context(null))
        {
            var platform = Platform(db);
            Assert.Equal(3, await platform.CollectGarbageAsync(allTenants: true));
            // A second pass finds nothing new.
            Assert.Equal(0, await platform.CollectGarbageAsync(allTenants: true));
        }

        await using (var check = Context(null))
        {
            var files = await AllFiles(check).ToDictionaryAsync(f => f.Id);
            foreach (var id in new[] { seed.OrphanA, seed.OrphanB, seed.ReleasedB })
            {
                Assert.Equal(FileRecordStatus.PendingPurge, files[id].Status);
                Assert.True(files[id].IsDeleted);
            }

            foreach (var id in new[] { seed.FreshA, seed.ReferencedA, seed.HeldA, seed.RetainedB })
            {
                Assert.False(files[id].IsDeleted);
                Assert.NotEqual(FileRecordStatus.PendingPurge, files[id].Status);
            }

            var items = await AllPurgeItems(check).ToListAsync();
            Assert.Equal(3, items.Count);
            Assert.All(items, i => Assert.Equal(files[i.FileId].TenantId, i.TenantId));
        }

        await using (var db = Context(null))
            Assert.Equal(3, await Platform(db).ProcessPurgeQueueAsync(allTenants: true));

        _storage.Verify(s => s.DeleteAsync(It.IsAny<FileReference>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        foreach (var id in new[] { seed.OrphanA, seed.OrphanB, seed.ReleasedB })
            _storage.Verify(s => s.DeleteAsync(It.Is<FileReference>(r => r.FileId == id), It.IsAny<CancellationToken>()), Times.Once);

        await using (var check = Context(null))
        {
            var deleted = await AllFiles(check).Where(f => f.Status == FileRecordStatus.Deleted).Select(f => f.Id).ToListAsync();
            Assert.Equal(
                new[] { seed.OrphanA, seed.OrphanB, seed.ReleasedB }.Order(),
                deleted.Order());
        }
    }

    [Fact]
    public async Task A_request_scoped_pass_stays_inside_its_tenant()
    {
        var seed = await SeedAsync();

        await using (var db = Context(TenantA))
            Assert.Equal(1, await Platform(db).CollectGarbageAsync());

        await using var check = Context(null);
        var statuses = await AllFiles(check).ToDictionaryAsync(f => f.Id, f => f.Status);
        Assert.Equal(FileRecordStatus.PendingPurge, statuses[seed.OrphanA]);
        Assert.Equal(FileRecordStatus.Pending, statuses[seed.OrphanB]);
        Assert.Equal(FileRecordStatus.Active, statuses[seed.ReleasedB]);
    }

    [Fact]
    public async Task Purge_aborts_when_a_reference_arrived_after_marking()
    {
        var seed = await SeedAsync();
        await using (var db = Context(null))
            await Platform(db).CollectGarbageAsync(allTenants: true);

        // The Register for OrphanB lands between marking and purging (e.g. a delayed outbox row).
        await using (var tenantB = Context(TenantB))
        {
            tenantB.FileReferences.Add(new FileReferenceRecord
            {
                FileId = seed.OrphanB,
                HolderKey = "hr-core:CandidateDocument:late:FileId",
                CreatedAt = DateTime.UtcNow,
                TenantId = TenantB
            });
            await tenantB.SaveChangesAsync();
        }

        await using (var db = Context(null))
            Assert.Equal(2, await Platform(db).ProcessPurgeQueueAsync(allTenants: true));

        _storage.Verify(s => s.DeleteAsync(It.Is<FileReference>(r => r.FileId == seed.OrphanB), It.IsAny<CancellationToken>()), Times.Never);
        await using var check = Context(null);
        var orphanB = await AllFiles(check).SingleAsync(f => f.Id == seed.OrphanB);
        Assert.False(orphanB.IsDeleted);
        Assert.Equal(FileRecordStatus.Active, orphanB.Status);
    }

    [Fact]
    public void All_tenant_collection_is_off_unless_configured()
    {
        Assert.False(FileGarbageCollectorJob.AllTenantsEnabled(new ConfigurationBuilder().Build()));
        Assert.True(FileGarbageCollectorJob.AllTenantsEnabled(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [FileGarbageCollectorJob.AllTenantsKey] = "true" })
            .Build()));
        Assert.Equal("FileStorage:GarbageCollection:AllTenants", FileGarbageCollectorJob.AllTenantsKey);
    }

    private sealed record Seed(
        Guid OrphanA, Guid FreshA, Guid ReferencedA, Guid HeldA, Guid OrphanB, Guid ReleasedB, Guid RetainedB);

    private async Task<Seed> SeedAsync()
    {
        await using var a = Context(TenantA);
        var orphanA = Add(a, TenantA, FileRecordStatus.Pending, uploadedAt: Old);
        var freshA = Add(a, TenantA, FileRecordStatus.Pending, uploadedAt: DateTime.UtcNow.AddHours(-1));
        var referencedA = Add(a, TenantA, FileRecordStatus.Active, uploadedAt: Old, referenceCount: 1);
        a.FileReferences.Add(new FileReferenceRecord
        {
            FileId = referencedA, HolderKey = "hr-core:CandidateDocument:a:FileId", CreatedAt = Old, TenantId = TenantA
        });
        var heldA = Add(a, TenantA, FileRecordStatus.Pending, uploadedAt: Old, legalHold: true);
        await a.SaveChangesAsync();

        await using var b = Context(TenantB);
        var orphanB = Add(b, TenantB, FileRecordStatus.Pending, uploadedAt: Old);
        var releasedB = Add(b, TenantB, FileRecordStatus.Active, uploadedAt: Old, updatedAt: Old);
        var retainedB = Add(b, TenantB, FileRecordStatus.Pending, uploadedAt: Old, retainUntil: DateTime.UtcNow.AddDays(30));
        await b.SaveChangesAsync();

        return new Seed(orphanA, freshA, referencedA, heldA, orphanB, releasedB, retainedB);
    }

    private static Guid Add(
        FileStorageDbContext db,
        Guid tenantId,
        string status,
        DateTime uploadedAt,
        int referenceCount = 0,
        bool legalHold = false,
        DateTime? updatedAt = null,
        DateTime? retainUntil = null)
    {
        var id = Guid.NewGuid();
        db.Files.Add(new FileRecord
        {
            Id = id,
            TenantId = tenantId,
            OriginalName = "cv.pdf",
            Extension = ".pdf",
            MimeType = "application/pdf",
            Size = 10,
            Sha256 = "00",
            Bucket = FileStorageBuckets.FileStorage,
            ObjectKey = $"2026/10/{id:N}.pdf",
            UploadedAt = uploadedAt,
            UpdatedAt = updatedAt ?? uploadedAt,
            Status = status,
            ScanStatus = FileScanStatus.Clean,
            ReferenceCount = referenceCount,
            LegalHold = legalHold,
            RetainUntil = retainUntil
        });
        return id;
    }

    private static IQueryable<FileRecord> AllFiles(FileStorageDbContext db) =>
        db.Files.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing).AsNoTracking();

    private static IQueryable<FilePurgeWorkItem> AllPurgeItems(FileStorageDbContext db) =>
        db.FilePurgeWorkItems.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing).AsNoTracking();

    /// <summary><c>null</c> is the GC job's scope: no tenant, production stamp interceptor.</summary>
    private FileStorageDbContext Context(Guid? tenantId)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(tenantId);
        return new FileStorageDbContext(
            new DbContextOptionsBuilder<FileStorageDbContext>()
                .UseSqlite(_connection)
                .AddInterceptors(new TenantIdStampInterceptor(tenant.Object))
                .Options,
            tenant.Object);
    }

    private FilePlatformService Platform(FileStorageDbContext db) =>
        new(db, _storage.Object, Mock.Of<IMalwareScanner>(), Mock.Of<IContentDisarmService>());
}
