using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.ExtPki;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Ext004ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "ext-pki", "pki-signature-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ExtPki:EnforceOnStartup"] = "true",
            ["ExtPki:LivePkiClaim"] = "false",
            ["ExtPki:MatrixPath"] = Path.Combine(RepoRoot, "docs", "ext-pki", "pki-signature-matrix.json")
        };
        foreach (var kv in overrides)
            values[kv.Key] = kv.Value;

        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment
        {
            EnvironmentName = environmentName,
            ContentRootPath = RepoRoot,
            ApplicationName = "tests",
            ContentRootFileProvider = new PhysicalFileProvider(RepoRoot)
        });
        services.AddExtPki(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["ExtPki:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IExtPkiProvider>();
        Assert.Equal("EXT-004", provider.Matrix.Card);
        Assert.True(provider.Matrix.SignerIntentRequired);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "national-pki-live" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "hsm-prod" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "tsa-live" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SYD-011" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SOV-003" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "STR-002" && b.Status == "Satisfied");
        Assert.True(provider.Matrix.MockSandbox?.LivePkiForbidden == true);
    }

    [Fact]
    public void Startup_fails_when_signer_intent_not_required()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext004-intent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtPkiMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-pki", "pki-signature-matrix.json")))!;
            matrix.SignerIntentRequired = false;
            var matrixPath = Path.Combine(tmp, "pki-signature-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtPki:EnforceOnStartup"] = "true",
                ["ExtPki:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtPkiStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("signerIntentRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_live_pki_not_forbidden()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext004-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtPkiMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-pki", "pki-signature-matrix.json")))!;
            matrix.MockSandbox!.LivePkiForbidden = false;
            var matrixPath = Path.Combine(tmp, "pki-signature-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtPki:EnforceOnStartup"] = "true",
                ["ExtPki:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtPkiStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("livePkiForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_private_key_may_leave_hsm()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext004-hsm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtPkiMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-pki", "pki-signature-matrix.json")))!;
            matrix.HsmCustody!.PrivateKeyNeverLeavesHsm = false;
            var matrixPath = Path.Combine(tmp, "pki-signature-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtPki:EnforceOnStartup"] = "true",
                ["ExtPki:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtPkiStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("privateKeyNeverLeavesHsm", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_form_signature_allowed_as_legal()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext004-form-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtPkiMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-pki", "pki-signature-matrix.json")))!;
            matrix.Signatures!.FormSignatureNotLegalEvidence = false;
            var matrixPath = Path.Combine(tmp, "pki-signature-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtPki:EnforceOnStartup"] = "true",
                ["ExtPki:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtPkiStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("formSignatureNotLegalEvidence", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LivePkiClaim_fails_until_workers()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["ExtPki:EnforceOnStartup"] = "true",
            ["ExtPki:LivePkiClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ExtPkiStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LivePkiClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["ExtPki:EnforceOnStartup"] = "true",
            ["ExtPki:LivePkiClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ExtPkiStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var guard = sp.GetRequiredService<IPkiSignatureClaimGuard>();
        Assert.Null(guard.EnsureLivePkiAllowed());
        Assert.NotNull(guard.ValidateSignerIntent(intentPresent: true, rolePresent: false));
        Assert.NotNull(guard.ValidateFormSignatureAsLegal(true));
        Assert.NotNull(guard.ValidateRevocation(revoked: true, treatedAsValid: true));
        Assert.NotNull(guard.ValidateExpiredLtv(expired: true, hasLtv: false, treatedAsValid: true));
        Assert.NotNull(guard.ValidateKeyExfil(true));
        Assert.NotNull(guard.ValidateAlgorithm("SHA1"));
        Assert.NotNull(guard.ValidateClockSkew(900));
        Assert.NotNull(guard.ValidateTsaOutage(tsaAvailable: false, treatedAsValid: true));
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "ext004-pki-signature-runtime-validate.ps1");
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -RepoRoot \"{RepoRoot}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(300_000);
        Assert.True(proc.ExitCode == 0, $"runtime validate failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_binding_docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-EXT-004-production-pki-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_004_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_004_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
