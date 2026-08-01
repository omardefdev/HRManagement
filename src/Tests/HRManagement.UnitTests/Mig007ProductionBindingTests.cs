using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.MigOnboarding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Mig007ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "mig-onboarding", "onboarding-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["MigOnboarding:EnforceOnStartup"] = "true",
            ["MigOnboarding:LiveDualAgencyOnboardClaim"] = "false",
            ["MigOnboarding:MatrixPath"] = Path.Combine(RepoRoot, "docs", "mig-onboarding", "onboarding-matrix.json")
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
        services.AddMigOnboarding(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["MigOnboarding:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IMigOnboardingProvider>();
        Assert.Equal("MIG-007", provider.Matrix.Card);
        Assert.True(provider.Matrix.ReadinessGates.Count >= 7);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "ONB-001" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "agency-extract-access" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-dual-agency-onboard" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "EXT-006" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "MIG-006" && b.Status == "Satisfied");
        Assert.True(provider.Matrix.Playbook?.NoforkRequired == true);
    }

    [Fact]
    public void Startup_fails_when_nofork_not_required()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mig007-nofork-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<MigOnboardingMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "mig-onboarding", "onboarding-matrix.json")))!;
            matrix.Playbook!.NoforkRequired = false;
            var matrixPath = Path.Combine(tmp, "onboarding-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["MigOnboarding:EnforceOnStartup"] = "true",
                ["MigOnboarding:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<MigOnboardingStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("noforkRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_wave_signoffs_not_required()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mig007-wave-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<MigOnboardingMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "mig-onboarding", "onboarding-matrix.json")))!;
            matrix.WaveCriteria!.SignOffsRequiredBeforeWave = false;
            var matrixPath = Path.Combine(tmp, "onboarding-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["MigOnboarding:EnforceOnStartup"] = "true",
                ["MigOnboarding:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<MigOnboardingStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("signOffsRequiredBeforeWave", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_readiness_gate_missing()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mig007-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<MigOnboardingMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "mig-onboarding", "onboarding-matrix.json")))!;
            matrix.ReadinessGates = matrix.ReadinessGates.Where(g => g.Id != "support").ToList();
            var matrixPath = Path.Combine(tmp, "onboarding-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["MigOnboarding:EnforceOnStartup"] = "true",
                ["MigOnboarding:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<MigOnboardingStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("support", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_live_portal_not_forbidden()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mig007-portal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<MigOnboardingMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "mig-onboarding", "onboarding-matrix.json")))!;
            matrix.SandboxCertification!.LivePortalForbidden = false;
            var matrixPath = Path.Combine(tmp, "onboarding-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["MigOnboarding:EnforceOnStartup"] = "true",
                ["MigOnboarding:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<MigOnboardingStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("livePortalForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LiveDualAgencyOnboardClaim_fails_until_workers()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["MigOnboarding:EnforceOnStartup"] = "true",
            ["MigOnboarding:LiveDualAgencyOnboardClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<MigOnboardingStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveDualAgencyOnboardClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["MigOnboarding:EnforceOnStartup"] = "true",
            ["MigOnboarding:LiveDualAgencyOnboardClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<MigOnboardingStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var guard = sp.GetRequiredService<IAgencyOnboardingClaimGuard>();
        Assert.Null(guard.EnsureLiveDualAgencyOnboardAllowed());
        Assert.NotNull(guard.ValidateNofork(true));
        Assert.NotNull(guard.ValidateWaveSignOffs(waveStarted: true, signOffsComplete: false));
        Assert.NotNull(guard.ValidateLivePortalClaim(true));
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "mig007-onboarding-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-MIG-007-production-onboarding-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_007_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_007_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
