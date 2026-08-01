using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Signatures;
using HRManagement.SharedKernel.Signatures;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace HRManagement.UnitTests;

public class Sig001SignatureTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "docs", "SIG_001_SIGNATURE_POLICY.md")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    [Fact]
    public void Same_payload_produces_same_hash()
    {
        var payload = new { employeeId = "E-001", amount = 5000m, currency = "IQD" };
        var hash1 = CanonicalPayloadBuilder.ComputeHash(payload);
        var hash2 = CanonicalPayloadBuilder.ComputeHash(payload);
        Assert.Equal(hash1, hash2);
        Assert.Matches("^[0-9a-f]{64}$", hash1);
    }

    [Fact]
    public void Key_order_does_not_affect_hash()
    {
        var hash1 = CanonicalPayloadBuilder.ComputeHash(new { a = 1, b = 2 });
        var hash2 = CanonicalPayloadBuilder.ComputeHash(new { b = 2, a = 1 });
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public async Task Sign_and_verify_roundtrip()
    {
        using var svc = CreateService();
        var payload = new { contractId = "C-100", employee = "E-200", salary = 3000m };
        var hash = CanonicalPayloadBuilder.ComputeHash(payload);

        var record = await svc.SignAsync(new SignatureRequest(
            Guid.NewGuid(), "Contract", "C-100", "user@gov.iq", "HR-Officer", "approve", hash));

        Assert.NotEmpty(record.SignatureBase64);
        Assert.NotEmpty(record.CertificateThumbprint);
        Assert.Equal("SHA256withRSA", record.Algorithm);
        Assert.True(svc.Verify(record));
    }

    [Fact]
    public async Task Tampered_payload_fails_verification()
    {
        using var svc = CreateService();
        var hash = CanonicalPayloadBuilder.ComputeHash(new { amount = 1000m });

        var record = await svc.SignAsync(new SignatureRequest(
            Guid.NewGuid(), "Payroll", "P-1", "signer", "Accountant", "approve", hash));

        Assert.True(svc.Verify(record));

        record.CanonicalPayloadHash = CanonicalPayloadBuilder.ComputeHash(new { amount = 9999m });
        Assert.False(svc.Verify(record));
    }

    [Fact]
    public async Task Update_and_delete_are_rejected()
    {
        await using var db = CreateDb();
        using var svc = CreateService();

        var hash = CanonicalPayloadBuilder.ComputeHash(new { test = true });
        var record = await svc.SignAsync(new SignatureRequest(
            Guid.NewGuid(), "Decision", "D-1", "admin", "Director", "approve", hash));

        db.Set<SignatureRecord>().Add(record);
        await db.SaveChangesAsync();

        var tracked = await db.Set<SignatureRecord>().SingleAsync(s => s.Id == record.Id);
        tracked.LtvStatus = "Tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();
        var again = await db.Set<SignatureRecord>().SingleAsync(s => s.Id == record.Id);
        db.Remove(again);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void Policy_rejects_unknown_subject_type()
    {
        Assert.False(SignaturePolicy.IsLegalSignatureRequired("UnknownType"));
        Assert.Null(SignaturePolicy.GetPolicy("UnknownType"));
    }

    [Fact]
    public void Policy_accepts_known_subject_types()
    {
        Assert.True(SignaturePolicy.IsLegalSignatureRequired("Contract"));
        Assert.True(SignaturePolicy.IsLegalSignatureRequired("Decision"));
        Assert.True(SignaturePolicy.IsLegalSignatureRequired("Payroll"));

        var contractPolicy = SignaturePolicy.GetPolicy("Contract");
        Assert.NotNull(contractPolicy);
        Assert.Equal(2, contractPolicy.MinSigners);
        Assert.True(contractPolicy.RequiresOrganizationalSeal);
    }

    [Fact]
    public void Docs_exist()
    {
        var root = ResolveRepoRoot();
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "SIG_001_SIGNATURE_POLICY.md")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "SIG_001_VERIFICATION.md")));
    }

    [Fact]
    public void Validate_script_exists()
    {
        var root = ResolveRepoRoot();
        Assert.True(System.IO.File.Exists(Path.Combine(root, "scripts", "sig001-signature-validate.ps1")));
    }

    private static SoftwareSignatureService CreateService()
    {
        var options = Options.Create(new SignatureOptions());
        return new SoftwareSignatureService(options);
    }

    private static SignatureProbeDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SignatureProbeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new SignatureImmutabilityInterceptor())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new SignatureProbeDbContext(options, DesignTimeTenantContext.Instance);
    }

    private sealed class SignatureProbeDbContext : TenantAwareDbContext
    {
        public SignatureProbeDbContext(DbContextOptions options, ITenantContext tenantContext)
            : base(options, tenantContext) { }

        public DbSet<SignatureRecord> SignatureRecords => Set<SignatureRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureSignatureRecords();
            base.OnModelCreating(modelBuilder);
        }
    }
}
