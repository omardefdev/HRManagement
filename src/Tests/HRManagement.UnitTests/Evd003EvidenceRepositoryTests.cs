using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HRManagement.ServiceDefaults.Evidence;
using HRManagement.SharedKernel.Evidence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace HRManagement.UnitTests;

/// <summary>EVD-003: immutable evidence repository behavior.</summary>
public class Evd003EvidenceRepositoryTests
{
    [Fact]
    public async Task Ingest_creates_artifact_with_sha256_hash()
    {
        await using var db = CreateDb();
        var root = Path.Combine(Path.GetTempPath(), "evd003-" + Guid.NewGuid().ToString("N"));
        var svc = CreateService(db, root);
        var content = Encoding.UTF8.GetBytes("control-evidence-body");

        var artifact = await svc.IngestAsync(new EvidenceIngestRequest(
            "art-SOV-001-01",
            "SOV-001",
            "rel-abc",
            "CI",
            content,
            ControlId: "CTRL-1"));

        var expected = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        Assert.Equal(expected, artifact.ContentHash);
        Assert.Equal("SOV-001", artifact.CardId);
        Assert.False(string.IsNullOrWhiteSpace(artifact.StorageUri));
        Assert.True(System.IO.File.Exists(artifact.StorageUri));

        var loaded = await svc.GetAsync("art-SOV-001-01");
        Assert.NotNull(loaded);
        Assert.Equal(expected, loaded!.ContentHash);
    }

    [Fact]
    public async Task Tamper_update_attempt_is_rejected()
    {
        await using var db = CreateDb();
        var svc = CreateService(db, Path.Combine(Path.GetTempPath(), "evd003-" + Guid.NewGuid().ToString("N")));
        var row = await svc.IngestAsync(new EvidenceIngestRequest(
            "art-tamper", "EVD-003", "r1", "Test", Encoding.UTF8.GetBytes("x")));

        var tracked = await db.Set<EvidenceArtifact>().SingleAsync(a => a.Id == row.Id);
        tracked.ContentHash = "deadbeef";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Expired_artifact_is_flagged_as_gap()
    {
        await using var db = CreateDb();
        var svc = CreateService(db, Path.Combine(Path.GetTempPath(), "evd003-" + Guid.NewGuid().ToString("N")));

        await svc.IngestAsync(new EvidenceIngestRequest(
            "art-fresh", "NAT-001", "r1", "Test", Encoding.UTF8.GetBytes("fresh"),
            ExpiresAtUtc: DateTime.UtcNow.AddDays(30)));

        await svc.IngestAsync(new EvidenceIngestRequest(
            "art-stale", "NAT-001", "r1", "Test", Encoding.UTF8.GetBytes("stale"),
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(-5)));

        var gaps = await svc.ListExpiredAsync();
        Assert.Contains(gaps, g => g.ArtifactId == "art-stale" && g.IsExpired);
        Assert.DoesNotContain(gaps, g => g.ArtifactId == "art-fresh");
    }

    [Fact]
    public async Task Export_pack_produces_hash_of_included_artifacts()
    {
        await using var db = CreateDb();
        var svc = CreateService(db, Path.Combine(Path.GetTempPath(), "evd003-" + Guid.NewGuid().ToString("N")));

        var a = await svc.IngestAsync(new EvidenceIngestRequest(
            "art-a", "CARD-A", "r1", "CI", Encoding.UTF8.GetBytes("aaa")));
        var b = await svc.IngestAsync(new EvidenceIngestRequest(
            "art-b", "CARD-A", "r1", "CI", Encoding.UTF8.GetBytes("bbb")));
        await svc.IngestAsync(new EvidenceIngestRequest(
            "art-other", "CARD-B", "r1", "CI", Encoding.UTF8.GetBytes("ccc")));

        var pack = await svc.ExportPackAsync(["CARD-A"], "tester", "local");
        var expected = EvidenceRepositoryService.ComputePackHash([a.ContentHash, b.ContentHash]);
        Assert.Equal(expected, pack.PackHash);
        Assert.Contains("art-a", pack.ArtifactIdsJson);
        Assert.Contains("art-b", pack.ArtifactIdsJson);
        Assert.DoesNotContain("art-other", pack.ArtifactIdsJson);
    }

    [Fact]
    public async Task No_delete_method_and_delete_throws()
    {
        var deleteMethods = typeof(IEvidenceRepositoryService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Empty(deleteMethods);

        await using var db = CreateDb();
        var svc = CreateService(db, Path.Combine(Path.GetTempPath(), "evd003-" + Guid.NewGuid().ToString("N")));
        var row = await svc.IngestAsync(new EvidenceIngestRequest(
            "art-del", "EVD-003", "r1", "Manual", Encoding.UTF8.GetBytes("keep")));

        db.ChangeTracker.Clear();
        var again = await db.Set<EvidenceArtifact>().SingleAsync(a => a.Id == row.Id);
        db.Remove(again);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    private static EvidenceRepositoryService CreateService(EvidenceRepoProbeDbContext db, string root) =>
        new(db, Options.Create(new EvidenceRepositoryOptions { LocalRootPath = root }));

    private static EvidenceRepoProbeDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<EvidenceRepoProbeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new EvidenceImmutabilityInterceptor())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new EvidenceRepoProbeDbContext(options);
    }

    private sealed class EvidenceRepoProbeDbContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<EvidenceArtifact> EvidenceArtifacts => Set<EvidenceArtifact>();
        public DbSet<EvidencePackExport> EvidencePackExports => Set<EvidencePackExport>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureEvidenceArtifacts();
            base.OnModelCreating(modelBuilder);
        }
    }
}
