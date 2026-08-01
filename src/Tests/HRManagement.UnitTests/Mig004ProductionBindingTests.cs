using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.MigMatch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Mig004ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "mig-match", "match-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["MigMatch:EnforceOnStartup"] = "true",
            ["MigMatch:LiveIdentityMatchClaim"] = "false",
            ["MigMatch:MatrixPath"] = Path.Combine(RepoRoot, "docs", "mig-match", "match-matrix.json")
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
        services.AddMigMatch(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["MigMatch:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IMigMatchProvider>();
        Assert.Equal("MIG-004", provider.Matrix.Card);
        Assert.True(provider.Matrix.MatchStrategies.Count >= 4);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "agency-extract-access" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-labelled-metrics" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "L10N-002" && b.Status == "Done");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "MIG-002" && b.Status == "Satisfied");
        Assert.True(provider.Matrix.Thresholds?.HighRiskAutoMergeForbidden == true);
    }

    [Fact]
    public void Startup_fails_when_high_risk_auto_merge_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mig004-hr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<MigMatchMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "mig-match", "match-matrix.json")))!;
            matrix.Thresholds!.HighRiskAutoMergeForbidden = false;
            var matrixPath = Path.Combine(tmp, "match-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["MigMatch:EnforceOnStartup"] = "true",
                ["MigMatch:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<MigMatchStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("highRiskAutoMergeForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_merge_not_reversible()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mig004-rev-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<MigMatchMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "mig-match", "match-matrix.json")))!;
            matrix.MergeAudit!.ReversibleRequired = false;
            var matrixPath = Path.Combine(tmp, "match-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["MigMatch:EnforceOnStartup"] = "true",
                ["MigMatch:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<MigMatchStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("reversibleRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_on_weak_review_privacy()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mig004-priv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<MigMatchMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "mig-match", "match-matrix.json")))!;
            matrix.ReviewPrivacy!.ForbidCrossAgencyPii = false;
            var matrixPath = Path.Combine(tmp, "match-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["MigMatch:EnforceOnStartup"] = "true",
                ["MigMatch:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<MigMatchStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("forbidCrossAgencyPii", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_signer_equals_reviewer()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mig004-sig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<MigMatchMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "mig-match", "match-matrix.json")))!;
            matrix.Signing!.ReviewerRole = matrix.Signing.SignedByRole;
            var matrixPath = Path.Combine(tmp, "match-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["MigMatch:EnforceOnStartup"] = "true",
                ["MigMatch:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<MigMatchStartupValidator>().Single();
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
    public void LiveIdentityMatchClaim_fails_until_workers()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["MigMatch:EnforceOnStartup"] = "true",
            ["MigMatch:LiveIdentityMatchClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<MigMatchStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveIdentityMatchClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["MigMatch:EnforceOnStartup"] = "true",
            ["MigMatch:LiveIdentityMatchClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<MigMatchStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var guard = sp.GetRequiredService<IIdentityMatchClaimGuard>();
        Assert.Null(guard.EnsureLiveIdentityMatchAllowed());
        Assert.NotNull(guard.ValidateProposedOutcome("AutoMerge", "High", null));
        Assert.NotNull(guard.ValidateReviewPrivacy(crossTenant: true));
        Assert.NotNull(guard.ValidateLiveMetricsClaim(liveAgencyEval: true));
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "mig004-match-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-MIG-004-production-match-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_004_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_004_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
