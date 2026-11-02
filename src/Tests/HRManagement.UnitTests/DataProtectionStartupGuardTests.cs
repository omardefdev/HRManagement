using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-39 (section 4, D-39-3, P-3): the Data Protection start-up guard (persistent, writable key ring; application
/// name; certificate present), the optional certificate protection with rotation and readable legacy keys, and the
/// start-up hook that runs the checks before the first migration (also when migrations are skipped at start-up).
/// </summary>
public sealed class DataProtectionStartupGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ops039-dp-" + Guid.NewGuid().ToString("N"));

    public DataProtectionStartupGuardTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Dir(string name) => Path.Combine(_root, name);

    [Fact]
    public void A_persistent_writable_key_ring_with_its_application_name_passes()
    {
        var failures = DataProtectionStartupGuard.Evaluate(new HrDataProtectionRegistration("HR.Core", Dir("ring"), null), "HR.Core");
        Assert.Empty(failures);
        Assert.Empty(Directory.GetFiles(Dir("ring")));   // the probe file is removed again
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_missing_key_ring_path_is_refused_with_the_service_name(string? path)
    {
        var failure = Assert.Single(DataProtectionStartupGuard.Evaluate(new HrDataProtectionRegistration("Payroll.Web", path, null), "Payroll.Web"));
        Assert.Contains("Payroll.Web", failure);
        Assert.Contains("DataProtection:KeyRingPath", failure);
    }

    [Fact]
    public void A_key_ring_path_that_cannot_be_written_is_refused_with_the_path()
    {
        var blocker = Path.Combine(_root, "not-a-directory");
        System.IO.File.WriteAllText(blocker, "a file where the directory should be");
        var failure = Assert.Single(DataProtectionStartupGuard.Evaluate(new HrDataProtectionRegistration("HR.Core", blocker, null), "HR.Core"));
        Assert.Contains(blocker, failure);
        Assert.Contains("not writable", failure);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("HR.Core", "HRManagement.Gateway")]
    public void A_missing_or_different_application_name_is_refused(string registered, string discriminator)
    {
        var failures = DataProtectionStartupGuard.Evaluate(new HrDataProtectionRegistration(registered, Dir("ring"), null), discriminator);
        Assert.Contains(failures, f => f.Contains("SetApplicationName"));
    }

    [Fact]
    public void A_configured_certificate_that_does_not_exist_is_refused()
    {
        var missing = Path.Combine(_root, "missing.pfx");
        var failure = Assert.Single(DataProtectionStartupGuard.Evaluate(new HrDataProtectionRegistration("HR.Core", Dir("ring"), missing), "HR.Core"));
        Assert.Contains(missing, failure);
    }

    [Theory]
    [InlineData("Production", null, true)]
    [InlineData("Acceptance", "true", true)]
    [InlineData("Acceptance", null, false)]
    [InlineData("Development", null, false)]
    public async Task The_guard_stops_the_start_where_it_is_enforced(string environment, string? requirePersistent, bool refuses)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [DataProtectionExtensions.RequirePersistentKeyRingKey] = requirePersistent,
        }).Build();
        var guard = new DataProtectionStartupGuard(configuration, new Ops039HostEnvironment(environment),
            new HrDataProtectionRegistration("IDCard.Web", null, null),
            Options.Create(new DataProtectionOptions { ApplicationDiscriminator = "IDCard.Web" }),
            NullLogger<DataProtectionStartupGuard>.Instance);

        if (refuses)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => guard.RunAsync(CancellationToken.None));
            Assert.Contains("IDCard.Web", error.Message);
        }
        else
        {
            await guard.RunAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void Certificate_protection_encrypts_new_keys_and_keeps_legacy_keys_and_rotated_certificates_readable()
    {
        var (certA, password) = MakeCertificate("a");
        var (certB, _) = MakeCertificate("b", password);

        // 1. A ring from before the certificate (unencrypted keys) stays readable once the certificate is configured.
        var legacyRing = Dir("legacy-ring");
        var legacy = Provider(legacyRing, null, null).CreateProtector("ops039").Protect("legacy");
        Assert.DoesNotContain(Directory.GetFiles(legacyRing, "key-*.xml"), f => System.IO.File.ReadAllText(f).Contains("encryptedSecret"));
        Assert.Equal("legacy", Provider(legacyRing, certA, password).CreateProtector("ops039").Unprotect(legacy));
        Provider(legacyRing, certA, password, forceNewKey: true);
        Assert.Contains(Directory.GetFiles(legacyRing, "key-*.xml"), f => System.IO.File.ReadAllText(f).Contains("encryptedSecret"));
        Assert.Equal("legacy", Provider(legacyRing, certA, password).CreateProtector("ops039").Unprotect(legacy));

        // 2. A ring born under certificate A: every key is encrypted at rest with A.
        var ring = Dir("ring");
        var underA = Provider(ring, certA, password).CreateProtector("ops039").Protect("under-a");
        var keys = Directory.GetFiles(ring, "key-*.xml");
        Assert.NotEmpty(keys);
        Assert.All(keys, f => Assert.Contains("encryptedSecret", System.IO.File.ReadAllText(f)));

        // 3. Rotation: certificate B is current, A is previous: values under A stay readable.
        Assert.Equal("under-a", Provider(ring, certB, password, previous: certA).CreateProtector("ops039").Unprotect(underA));

        // 4. Without A among the certificates, the keys it encrypted cannot be read.
        Assert.ThrowsAny<CryptographicException>(() => Provider(ring, certB, password).CreateProtector("ops039").Unprotect(underA));
    }

    private IDataProtectionProvider Provider(string ring, string? certificate, string? password, string? previous = null, bool forceNewKey = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [DataProtectionExtensions.KeyRingPathKey] = ring,
            [DataProtectionExtensions.CertificatePathKey] = certificate,
            [DataProtectionExtensions.CertificatePasswordKey] = password,
            [DataProtectionExtensions.PreviousCertificatePathsKey] = previous,
        });
        builder.AddHrDataProtection("Ops039.Tests");
        var services = builder.Services.BuildServiceProvider();
        if (forceNewKey)
        {
            // A key activated after the legacy one becomes the default (the legacy key stays valid for reading).
            var manager = services.GetRequiredService<Microsoft.AspNetCore.DataProtection.KeyManagement.IKeyManager>();
            manager.CreateNewKey(DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow.AddDays(90));
        }

        return services.GetRequiredService<IDataProtectionProvider>();
    }

    private (string Path, string Password) MakeCertificate(string name, string? password = null)
    {
        password ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest($"CN=ops039-{name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var path = Path.Combine(_root, $"{name}.pfx");
        System.IO.File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12, password));
        return (path, password);
    }
}

/// <summary>PLAN-39 (P-3): the start-up checks run before the first migration, also with migrations skipped at start-up.</summary>
public class HrStartupChecksOrderingTests
{
    private sealed class Recorder
    {
        public List<string> Events { get; } = [];
    }

    private sealed class RecordingCheck(Recorder recorder) : IHrStartupCheck
    {
        public string Name => "recording";

        public Task RunAsync(CancellationToken cancellationToken)
        {
            recorder.Events.Add("check");
            return Task.CompletedTask;
        }
    }

    private sealed class RefusingCheck(Recorder recorder) : IHrStartupCheck
    {
        public string Name => "refusing";

        public Task RunAsync(CancellationToken cancellationToken)
        {
            recorder.Events.Add("refused");
            throw new InvalidOperationException("ops039 refusal");
        }
    }

    private sealed class ProbeContext : DbContext
    {
        public ProbeContext(DbContextOptions<ProbeContext> options, Recorder recorder) : base(options) => recorder.Events.Add("context");
    }

    private static IServiceProvider Services(bool runMigrations, bool refuse, Recorder recorder)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:RunMigrationsAtStartup"] = runMigrations ? "true" : "false",
        }).Build());
        services.AddSingleton<IHostEnvironment>(new Ops039HostEnvironment("Development"));
        services.AddSingleton(recorder);
        services.AddHrStartupCheck<RecordingCheck>();
        if (refuse) services.AddHrStartupCheck<RefusingCheck>();
        // Port 1 refuses the connection at once: the (non-production) migration failure is only a warning.
        services.AddDbContext<ProbeContext>(o => o.UseNpgsql("Host=127.0.0.1;Port=1;Database=ops039;Username=x;Password=x;Timeout=2"));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Checks_run_before_the_context_of_the_first_migration_is_created()
    {
        var recorder = new Recorder();
        await Services(runMigrations: true, refuse: false, recorder).TryMigrateAsync<ProbeContext>();
        Assert.Equal(["check", "context"], recorder.Events);
    }

    [Fact]
    public async Task A_refusing_check_stops_the_start_before_any_migration_even_outside_production()
    {
        var recorder = new Recorder();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Services(runMigrations: true, refuse: true, recorder).TryMigrateAsync<ProbeContext>());
        Assert.Equal("ops039 refusal", error.Message);
        Assert.Equal(["check", "refused"], recorder.Events);
    }

    [Fact]
    public async Task Checks_also_run_when_migrations_are_skipped_at_start_up()
    {
        var recorder = new Recorder();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Services(runMigrations: false, refuse: true, recorder).TryMigrateAsync<ProbeContext>());
        Assert.Equal(["check", "refused"], recorder.Events);
    }

    [Fact]
    public async Task Checks_run_once_although_a_host_migrates_two_contexts()
    {
        var recorder = new Recorder();
        var services = Services(runMigrations: true, refuse: false, recorder);
        await services.TryMigrateAsync<ProbeContext>();
        await services.TryMigrateAsync<ProbeContext>();
        await HrStartupChecks.RunOnceAsync(services);
        Assert.Single(recorder.Events, "check");
    }

    /// <summary>Every host registers its Data Protection key ring and migrates (the hook) before its first seed.</summary>
    [Fact]
    public void Every_program_runs_the_hook_before_its_first_seed()
    {
        var programs = Directory.GetFiles(Path.Combine(HRManagement.TestSupport.RepoPaths.Root, "src"), "Program.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && System.IO.File.ReadAllText(p).Contains("AddHrDataProtection(", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(15, programs.Count);
        foreach (var program in programs)
        {
            var text = System.IO.File.ReadAllText(program);
            var hook = text.IndexOf("TryMigrateAsync<", StringComparison.Ordinal);
            Assert.True(hook > 0, $"{program}: no TryMigrateAsync");
            foreach (var seedMarker in new[] { "new SeedRunner(", "RunMandatoryAsync(", "RunOptionalAsync(" })
            {
                var seed = text.IndexOf(seedMarker, StringComparison.Ordinal);
                Assert.True(seed < 0 || seed > hook, $"{program}: {seedMarker} before TryMigrateAsync");
            }
        }

        var hr = System.IO.File.ReadAllText(programs.Single(p => p.Contains("HR.Core.Web", StringComparison.Ordinal)));
        Assert.Contains("AddHrStartupCheck<HR.Core.Infrastructure.Data.EmployeePiiKeyRingCanary>()", hr);
    }
}
