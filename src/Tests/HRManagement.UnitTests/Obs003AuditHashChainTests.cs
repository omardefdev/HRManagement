using HRManagement.Gateway.Data;
using HRManagement.Gateway.Models;
using HRManagement.Gateway.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.UnitTests;

public class Obs003AuditHashChainTests
{
    private static GatewayAuditEntry Sample(long seq, string path = "/api/x", string decision = "allowed") => new()
    {
        OccurredAtUtc = DateTimeOffset.Parse("2026-07-28T12:00:00Z"),
        SequenceNumber = seq,
        CorrelationId = "corr",
        Method = "GET",
        Path = path,
        QueryString = "",
        StatusCode = 200,
        ElapsedMs = 1,
        Decision = decision,
        ChainKey = AuditHashChain.DefaultChainKey
    };

    private static ServiceProvider CreateProvider(string dbName)
    {
        var root = new InMemoryDatabaseRoot();
        var services = new ServiceCollection();
        services.AddDbContext<GatewayAuditDbContext>(o =>
            o.UseInMemoryDatabase(dbName, root));
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Seal_And_Verify_RoundTrip()
    {
        var e = Sample(1);
        AuditHashChain.Seal(e, AuditHashChain.GenesisPrevHash);
        Assert.False(string.IsNullOrEmpty(e.EventHash));
        Assert.Equal(AuditHashChain.GenesisPrevHash, e.PrevHash);
        Assert.True(AuditHashChain.Verify(e));
    }

    [Fact]
    public void FieldTamper_BreaksVerify()
    {
        var e = Sample(1);
        AuditHashChain.Seal(e, AuditHashChain.GenesisPrevHash);
        e.Decision = "tampered";
        Assert.False(AuditHashChain.Verify(e));
    }

    [Fact]
    public async Task Insert_SealsChain_AndVerifierOk()
    {
        await using var sp = CreateProvider("obs003-chain-" + Guid.NewGuid().ToString("N"));
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GatewayAuditDbContext>();
            var a = Sample(1);
            var b = Sample(2, "/api/y");
            AuditHashChain.Seal(a, AuditHashChain.GenesisPrevHash);
            AuditHashChain.Seal(b, a.EventHash);
            db.AuditEntries.AddRange(a, b);
            await db.SaveChangesAsync();
        }

        var verifier = new AuditChainVerifier(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AuditChainVerifier>.Instance);
        var result = await verifier.VerifyAsync();
        Assert.True(result.Ok, result.Error);
        Assert.Equal(2, result.Checked);
    }

    [Fact]
    public async Task Verifier_Detects_BrokenEventHash()
    {
        await using var sp = CreateProvider("obs003-tamper-" + Guid.NewGuid().ToString("N"));
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GatewayAuditDbContext>();
            var good = Sample(1);
            AuditHashChain.Seal(good, AuditHashChain.GenesisPrevHash);
            db.AuditEntries.Add(good);
            await db.SaveChangesAsync();

            var broken = Sample(2, "/api/broken");
            broken.PrevHash = good.EventHash;
            broken.EventHash = "0000000000000000000000000000000000000000000000000000000000000000";
            db.AuditEntries.Add(broken);
            await db.SaveChangesAsync();
            Assert.False(AuditHashChain.Verify(broken));
        }

        var verifier = new AuditChainVerifier(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AuditChainVerifier>.Instance);
        var result = await verifier.VerifyAsync();
        Assert.False(result.Ok, "expected chain verify failure: " + result.Error);
        Assert.Contains("EventHash", result.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SiemRuleLoader_ParsesYaml()
    {
        var rule = SiemRuleLoader.ParseSimpleYaml("""
            id: bulk-lookup
            eventType: BulkLookup
            windowMinutes: 5
            threshold: 3
            alertname: SiemBulkLookup
            severity: page
            """);
        Assert.NotNull(rule);
        Assert.Equal("BulkLookup", rule!.EventType);
        Assert.Equal(3, rule.Threshold);
        Assert.Equal("SiemBulkLookup", rule.AlertName);
    }

    [Fact]
    public void LegalHold_BlocksPurge_WhenGlobal()
    {
        var hold = new AuditLegalHold { Active = true, Reason = "case" };
        Assert.True(GatewayAuditRetentionService.HoldBlocksPurge(hold, DateTimeOffset.UtcNow.AddDays(-30)));
    }
}
