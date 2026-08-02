using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.MigEvidence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Mig005ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "mig-evidence", "recon-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["MigEvidence:EnforceOnStartup"] = "true",
            ["MigEvidence:LiveAgencyReconClaim"] = "false",
            ["MigEvidence:MatrixPath"] = Path.Combine(RepoRoot, "docs", "mig-evidence", "recon-matrix.json")
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
        services.AddMigEvidence(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["MigEvidence:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IMigEvidenceProvider>();
        Assert.Equal("MIG-005", provider.Matrix.Card);
        Assert.True(provider.Matrix.AccountedBuckets.Count >= 3);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "agency-extract-access" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-agency-recon" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-independent-sql" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "MIG-003" && b.Status == "Satisfied");
        Assert.True(provider.Matrix.EvidencePackage?.Immutable == true);
    }

    [Fact]
    public void Startup_fails_when_unexplained_variance_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mig005-fin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<MigEvidenceMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "mig-evidence", "recon-matrix.json")))!;
            matrix.FinancialPolicy!.UnexplainedVarianceForbidden = false;
            var matrixPath = Path.Combine(tmp, "recon-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["MigEvidence:EnforceOnStartup"] = "true",
                ["MigEvidence:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<MigEvidenceStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("unexplainedVarianceForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_dual_control_min_signers_too_low()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mig005-dual-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<MigEvidenceMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "mig-evidence", "recon-matrix.json")))!;
            matrix.DualControl!.MinSigners = 1;
            var matrixPath = Path.Combine(tmp, "recon-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["MigEvidence:EnforceOnStartup"] = "true",
                ["MigEvidence:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<MigEvidenceStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("minSigners", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_evidence_not_immutable()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mig005-imm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<MigEvidenceMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "mig-evidence", "recon-matrix.json")))!;
            matrix.EvidencePackage!.Immutable = false;
            var matrixPath = Path.Combine(tmp, "recon-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["MigEvidence:EnforceOnStartup"] = "true",
                ["MigEvidence:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<MigEvidenceStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("immutable", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_signer_equals_reviewer()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mig005-sig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<MigEvidenceMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "mig-evidence", "recon-matrix.json")))!;
            matrix.Signing!.ReviewerRole = matrix.Signing.SignedByRole;
            var matrixPath = Path.Combine(tmp, "recon-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["MigEvidence:EnforceOnStartup"] = "true",
                ["MigEvidence:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<MigEvidenceStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("reviewer", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LiveAgencyReconClaim_fails_until_workers()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["MigEvidence:EnforceOnStartup"] = "true",
            ["MigEvidence:LiveAgencyReconClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<MigEvidenceStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveAgencyReconClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["MigEvidence:EnforceOnStartup"] = "true",
            ["MigEvidence:LiveAgencyReconClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<MigEvidenceStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var guard = sp.GetRequiredService<IAgencyReconClaimGuard>();
        Assert.Null(guard.EnsureLiveAgencyReconAllowed());
        Assert.NotNull(guard.ValidateFinancialVariance(1250));
        Assert.NotNull(guard.ValidateEvidenceImmutability(true, hashMatch: false));
        Assert.NotNull(guard.ValidateIndependentSqlClaim(true));
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "mig005-evidence-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-MIG-005-production-evidence-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_005_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_005_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
