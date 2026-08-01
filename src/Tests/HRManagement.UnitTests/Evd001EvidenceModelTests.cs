using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Evidence;
using HRManagement.SharedKernel.Evidence;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRManagement.UnitTests;

public class Evd001EvidenceModelTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "docs", "ADR-EVD-001-unified-evidence-model.md")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    [Fact]
    public void Hash_chain_seals_and_detects_tamper()
    {
        var a = new EvidenceRecord
        {
            TenantId = Guid.NewGuid(),
            CorrelationId = "corr-1",
            Actor = "user-a",
            Resource = "Employee/1",
            Purpose = "hire",
            Result = "Completed",
            Sequence = 1
        };
        EvidenceHashChain.Seal(a, EvidenceHashChain.GenesisPrevHash);
        Assert.True(EvidenceHashChain.Verify(a));

        a.Result = "Tampered";
        Assert.False(EvidenceHashChain.Verify(a));
    }

    [Fact]
    public async Task Collector_appends_linked_sequence()
    {
        await using var db = CreateDb();
        var collector = new EvidenceCollector(db);
        var tenant = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

        var r1 = await collector.RecordAsync(new EvidenceWriteRequest(
            tenant, "hire-1", "hr-officer", "Employee/x", "hire.decide", "Completed"));
        var r2 = await collector.RecordAsync(new EvidenceWriteRequest(
            tenant, "hire-1", "hr-manager", "Contract/y", "hire.contract", "Completed",
            CausationId: r1.Id.ToString("N")));

        Assert.Equal(1, r1.Sequence);
        Assert.Equal(2, r2.Sequence);
        Assert.Equal(r1.EventHash, r2.PrevHash);
        Assert.True(EvidenceHashChain.Verify(r1));
        Assert.True(EvidenceHashChain.Verify(r2));
    }

    [Fact]
    public async Task Update_and_delete_are_rejected()
    {
        await using var db = CreateDb();
        var collector = new EvidenceCollector(db);
        var row = await collector.RecordAsync(new EvidenceWriteRequest(
            Guid.NewGuid(), "c", "a", "r", "p", "ok"));

        var tracked = await db.Set<EvidenceRecord>().SingleAsync(e => e.Id == row.Id);
        tracked.Result = "nope";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();
        var again = await db.Set<EvidenceRecord>().SingleAsync(e => e.Id == row.Id);
        db.Remove(again);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void Docs_exist()
    {
        var root = ResolveRepoRoot();
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "ADR-EVD-001-unified-evidence-model.md")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "EVD_001_EVIDENCE_SCHEMA.md")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "EVD_001_VERIFICATION.md")));
    }

    private static EvidenceProbeDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<EvidenceProbeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new EvidenceImmutabilityInterceptor())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new EvidenceProbeDbContext(options, DesignTimeTenantContext.Instance);
    }

    private sealed class EvidenceProbeDbContext : TenantAwareDbContext
    {
        public EvidenceProbeDbContext(DbContextOptions options, ITenantContext tenantContext)
            : base(options, tenantContext) { }

        public DbSet<EvidenceRecord> EvidenceRecords => Set<EvidenceRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureEvidenceRecords();
            base.OnModelCreating(modelBuilder);
        }
    }
}
