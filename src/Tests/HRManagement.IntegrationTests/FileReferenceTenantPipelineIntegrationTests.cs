using File.Storage.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.IntegrationTests;

/// <summary>
/// File-reference pipeline per tenant on PostgreSQL: HR.Core's outbox dispatcher, running in a background scope with no
/// tenant, delivers each tenant's Register/Release rows to the real File.Storage host with that row's X-Tenant-ID, so the
/// files become Active (or are released) in their own tenant only.
/// </summary>
[Trait("Category", "Testcontainers")]
public class FileReferenceTenantPipelineIntegrationTests(FileReferenceTenantPipelineFixture fixture)
    : IClassFixture<FileReferenceTenantPipelineFixture>
{
    [SkippableFact]
    public async Task Background_dispatcher_registers_and_releases_each_tenants_files_in_their_own_tenant()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var fileA = await fixture.ServiceUploadAsync(tenantA);
        var fileB = await fixture.ServiceUploadAsync(tenantB);
        var holderA = $"hr-core:CandidateDocument:{Guid.NewGuid():N}:FileId";
        var holderB = $"hr-core:OfferLetter:{Guid.NewGuid():N}:AcceptanceEvidence";
        // Tenant B naming tenant A's file: File.Storage must not find it in B.
        var crossHolder = $"hr-core:CandidateDocument:{Guid.NewGuid():N}:FileId";

        await EnqueueAsync(tenantA, o => o.EnqueueRegister(fileA, holderA));
        await EnqueueAsync(tenantB, o =>
        {
            o.EnqueueRegister(fileB, holderB);
            o.EnqueueRegister(fileA, crossHolder);
        });

        await using var services = fixture.CreateHrCoreDeliveryServices();
        Assert.Equal(2, await DispatchAsync(services));

        await AssertOutboxAsync(tenantA, holderA, FileReferenceOutboxStatus.Succeeded);
        await AssertOutboxAsync(tenantB, holderB, FileReferenceOutboxStatus.Succeeded);
        var cross = await AssertOutboxAsync(tenantB, crossHolder, FileReferenceOutboxStatus.Pending);
        Assert.Equal(1, cross.Attempt);
        Assert.Contains("404", cross.LastError, StringComparison.Ordinal);

        await AssertFileAsync(tenantA, fileA, FileRecordStatus.Active, referenceCount: 1, holders: [holderA]);
        await AssertFileAsync(tenantB, fileB, FileRecordStatus.Active, referenceCount: 1, holders: [holderB]);
        await using (var files = fixture.CreateFileStorageContext(tenantB))
            Assert.False(await files.Files.AnyAsync(f => f.Id == fileA));

        // Release (e.g. the retention job) travels the same way and frees the file in its tenant.
        await EnqueueAsync(tenantB, o => o.EnqueueRelease(fileB, holderB));
        Assert.Equal(1, await DispatchAsync(services));

        await using (var files = fixture.CreateFileStorageContext(tenantB))
        {
            var record = await files.Files.AsNoTracking()
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
                .SingleAsync(f => f.Id == fileB);
            Assert.Equal(0, record.ReferenceCount);
            Assert.Equal(FileRecordStatus.PendingPurge, record.Status);
            Assert.False(await files.FileReferences.AnyAsync(r => r.FileId == fileB));
            Assert.True(await files.FilePurgeWorkItems.AnyAsync(p => p.FileId == fileB && p.TenantId == tenantB));
            Assert.All(await files.FileAccessGrants.Where(g => g.FileId == fileB).ToListAsync(), g => Assert.True(g.IsRevoked));
        }

        await AssertFileAsync(tenantA, fileA, FileRecordStatus.Active, referenceCount: 1, holders: [holderA]);
    }

    private async Task EnqueueAsync(Guid tenantId, Action<EfFileReferenceOutbox> enqueue)
    {
        await using var db = fixture.CreateHrCoreContext(tenantId);
        enqueue(new EfFileReferenceOutbox(db, new FixedTenant(tenantId)));
        await db.SaveChangesAsync();
    }

    /// <summary>One dispatcher cycle exactly as the hosted job runs it: background scope, advisory lock, HTTP delivery.</summary>
    private async Task<int> DispatchAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await using var db = fixture.CreateHrCoreContext(null);
        var processed = -1;
        var acquired = await AdvisoryLeaderElection.TryRunAsLeaderAsync(
            db,
            AdvisoryLockKeys.FileReferenceOutbox,
            async ct => processed = await FileReferenceOutboxDispatcherJob<HRCoreDbContext>.ProcessBatchAsync(
                db, scope.ServiceProvider.GetRequiredService<HttpFileReferenceClient>(), ct));
        Assert.True(acquired);
        return processed;
    }

    private async Task<FileReferenceOutboxMessage> AssertOutboxAsync(Guid tenantId, string holderKey, string status)
    {
        await using var db = fixture.CreateHrCoreContext(tenantId);
        var row = await db.FileReferenceOutbox.AsNoTracking().SingleAsync(o => o.HolderKey == holderKey);
        Assert.Equal(status, row.Status);
        Assert.Equal(tenantId, row.TenantId);
        return row;
    }

    private async Task AssertFileAsync(Guid tenantId, Guid fileId, string status, int referenceCount, string[] holders)
    {
        await using var files = fixture.CreateFileStorageContext(tenantId);
        var record = await files.Files.AsNoTracking().SingleAsync(f => f.Id == fileId);
        Assert.Equal(tenantId, record.TenantId);
        Assert.Equal(status, record.Status);
        Assert.Equal(referenceCount, record.ReferenceCount);
        var references = await files.FileReferences.AsNoTracking().Where(r => r.FileId == fileId).ToListAsync();
        Assert.Equal(holders.Order(StringComparer.Ordinal), references.Select(r => r.HolderKey).Order(StringComparer.Ordinal));
        Assert.All(references, r => Assert.Equal(tenantId, r.TenantId));
    }

    private sealed class FixedTenant(Guid tenantId) : HRManagement.SharedKernel.Tenancy.ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }
}
