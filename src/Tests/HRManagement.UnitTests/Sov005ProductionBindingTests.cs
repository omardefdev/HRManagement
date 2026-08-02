using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.SovThreat;
using HRManagement.ServiceDefaults.SovZt;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Sov005ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "sov-threat", "threat-model-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["SovThreat:EnforceOnStartup"] = "true",
            ["SovThreat:LiveThreatModelClaim"] = "false",
            ["SovThreat:MatrixPath"] = Path.Combine(RepoRoot, "docs", "sov-threat", "threat-model-matrix.json")
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
        services.AddSovThreat(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["SovThreat:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<ISovThreatProvider>();
        Assert.Equal("SOV-005", provider.Matrix.Card);
        Assert.True(provider.Matrix.Methodology?.StrideRequired == true);
        Assert.True(provider.Matrix.HighThreatCoverage?.ControlRequired == true);
        Assert.True(provider.Matrix.ResidualRisk?.SignedRequired == true);
        Assert.True(provider.Matrix.MockSandbox?.LiveIndependentTmReviewForbidden == true);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-independent-tm-review" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SOV-006" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "NAT-001" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SOV-004" && b.Status == "Satisfied");
    }

    [Fact]
    public void Startup_fails_when_stride_not_required()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov005-st-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovThreatMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-threat", "threat-model-matrix.json")))!;
            matrix.Methodology!.StrideRequired = false;
            var matrixPath = Path.Combine(tmp, "threat-model-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovThreat:EnforceOnStartup"] = "true",
                ["SovThreat:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovThreatStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("strideRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void Startup_fails_when_high_without_control()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov005-hc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovThreatMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-threat", "threat-model-matrix.json")))!;
            matrix.HighThreatCoverage!.ControlRequired = false;
            var matrixPath = Path.Combine(tmp, "threat-model-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovThreat:EnforceOnStartup"] = "true",
                ["SovThreat:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovThreatStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("controlRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void Startup_fails_when_residual_unsigned()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov005-ru-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovThreatMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-threat", "threat-model-matrix.json")))!;
            matrix.ResidualRisk!.SignedRequired = false;
            var matrixPath = Path.Combine(tmp, "threat-model-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovThreat:EnforceOnStartup"] = "true",
                ["SovThreat:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovThreatStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("signedRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void LiveThreatModelClaim_fails_until_live_review()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["SovThreat:EnforceOnStartup"] = "true",
            ["SovThreat:LiveThreatModelClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<SovThreatStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveThreatModelClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix_and_live_Blocked()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["SovThreat:EnforceOnStartup"] = "true",
            ["SovThreat:LiveThreatModelClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<SovThreatStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var provider = sp.GetRequiredService<ISovThreatProvider>();
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-independent-tm-review" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SOV-006" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "NAT-001" && b.Status == "NotStarted");

        var guard = sp.GetRequiredService<IThreatModelClaimGuard>();
        Assert.Null(guard.EnsureLiveThreatModelAllowed());
        Assert.NotNull(guard.ValidateMethodology(false));
        Assert.NotNull(guard.ValidateHighThreatCoverage(false, true, true));
        Assert.NotNull(guard.ValidateResidualRisk(false));
        Assert.Null(guard.ValidateMethodology(true));
        Assert.Null(guard.ValidateHighThreatCoverage(true, true, true));
    }

    [Fact]
    public void Sov004_accepts_SOV005_Satisfied_without_raising_LivePam()
    {
        using var sov4 = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "sov-zt", "zero-trust-matrix.json")));
        var s4 = sov4.RootElement.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", s4["SOV-005"]);
        Assert.Equal("NotStarted", s4["NAT-001"]);

        var failures = SovZtEvaluator.EvaluateBlockers(
            JsonSerializer.Deserialize<SovZtMatrixDocument>(
                System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "sov-zt", "zero-trust-matrix.json")))!);
        Assert.DoesNotContain(failures, f => f.Contains("SOV-005", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "sov005-threat-model-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-SOV-005-production-threat-modeling-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_005_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_005_INDEPENDENT_REVIEW_WAIVER.md")));
        var backlog = System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "completion-backlog", "08-NATIONAL-ASSURANCE-OPERATIONS.md"));
        Assert.Contains("Done (production threat-modeling binding)", backlog, StringComparison.Ordinal);
    }
}
