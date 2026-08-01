using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.ProgramGovernance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Str005ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "str-governance", "governance-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ProgramGovernance:EnforceOnStartup"] = "true",
            ["ProgramGovernance:GoLiveDeclared"] = "false",
            ["ProgramGovernance:MatrixPath"] = Path.Combine(RepoRoot, "docs", "str-governance", "governance-matrix.json"),
            ["ProgramGovernance:DecisionLogPath"] = Path.Combine(RepoRoot, "docs", "str-governance", "decision-log.json"),
            ["ProgramGovernance:RiskRegisterPath"] = Path.Combine(RepoRoot, "docs", "str-governance", "risk-register.json"),
            ["ProgramGovernance:GoLiveEvidencePath"] = Path.Combine(
                RepoRoot, "src", "Shared", "HRManagement.ServiceDefaults", "ProgramGovernance", "go-live-evidence.json")
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
        services.AddProgramGovernance(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_decision_log_and_risk_register()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["ProgramGovernance:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IProgramGovernanceProvider>();
        Assert.Equal("STR-005", provider.Matrix.Card);
        Assert.Equal("STR-005", provider.DecisionLog.Card);
        Assert.Equal("STR-005", provider.RiskRegister.Card);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SYD-017" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "PGM-001" && b.Status == "Satisfied");
        Assert.NotEmpty(provider.DecisionLog.Decisions);
        Assert.NotNull(provider.GoLiveEvidence);
    }

    [Fact]
    public void Startup_fails_on_expired_declared_exception()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "str005-exp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<GovernanceMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "str-governance", "governance-matrix.json")))!;
            matrix.Exceptions[0].ExpiresOn = "2020-01-01";
            matrix.Exceptions[0].Status = "Declared";
            var matrixPath = Path.Combine(tmp, "governance-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProgramGovernance:EnforceOnStartup"] = "true",
                ["ProgramGovernance:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProgramGovernanceStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("STR-005", ex.Message, StringComparison.Ordinal);
            Assert.Contains("expired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void GoLiveDeclared_fails_with_eng_only_evidence()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["ProgramGovernance:EnforceOnStartup"] = "true",
            ["ProgramGovernance:GoLiveDeclared"] = "true",
            ["ProgramGovernance:GoLiveEvidencePath"] = Path.Combine(RepoRoot, "testdata", "str005", "go-live", "eng-only.json")
        });
        var validator = sp.GetServices<IHostedService>().OfType<ProgramGovernanceStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("eng-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GoLiveDeclared_passes_with_multi_seat_evidence()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["ProgramGovernance:EnforceOnStartup"] = "true",
            ["ProgramGovernance:GoLiveDeclared"] = "true",
            ["ProgramGovernance:GoLiveEvidencePath"] = Path.Combine(RepoRoot, "testdata", "str005", "go-live", "multi-seat.json")
        });
        var validator = sp.GetServices<IHostedService>().OfType<ProgramGovernanceStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var guard = sp.GetRequiredService<IGoLiveSeatGuard>();
        Assert.Null(guard.EnsureGoLiveAllowed());
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "str005-governance-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-STR-005-production-governance-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_005_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_005_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
