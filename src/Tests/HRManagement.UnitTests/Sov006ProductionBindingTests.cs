using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.SovPentest;
using HRManagement.ServiceDefaults.SovThreat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Sov006ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "sov-pentest", "pen-red-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["SovPentest:EnforceOnStartup"] = "true",
            ["SovPentest:LivePenClaim"] = "false",
            ["SovPentest:MatrixPath"] = Path.Combine(RepoRoot, "docs", "sov-pentest", "pen-red-matrix.json")
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
        services.AddSovPentest(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["SovPentest:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<ISovPentestProvider>();
        Assert.Equal("SOV-006", provider.Matrix.Card);
        Assert.True(provider.Matrix.Independence?.AssessorIndependentRequired == true);
        Assert.True(provider.Matrix.FindingClosure?.CriticalHighZeroOpenRequired == true);
        Assert.True(provider.Matrix.PurpleDetection?.SocDetectionRequired == true);
        Assert.True(provider.Matrix.MockSandbox?.LiveIndependentPenAssessmentForbidden == true);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-independent-pen-assessment" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "NAT-001" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "NAT-002" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SOV-005" && b.Status == "Satisfied");
    }

    [Fact]
    public void Startup_fails_when_open_critical_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov006-oc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovPentestMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-pentest", "pen-red-matrix.json")))!;
            matrix.FindingClosure!.CriticalHighZeroOpenRequired = false;
            var matrixPath = Path.Combine(tmp, "pen-red-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovPentest:EnforceOnStartup"] = "true",
                ["SovPentest:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovPentestStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("criticalHighZeroOpenRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void Startup_fails_when_assessor_not_independent()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov006-ai-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovPentestMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-pentest", "pen-red-matrix.json")))!;
            matrix.Independence!.AssessorIndependentRequired = false;
            var matrixPath = Path.Combine(tmp, "pen-red-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovPentest:EnforceOnStartup"] = "true",
                ["SovPentest:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovPentestStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("assessorIndependentRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void Startup_fails_when_retest_not_required()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov006-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovPentestMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-pentest", "pen-red-matrix.json")))!;
            matrix.FindingClosure!.RetestRequired = false;
            var matrixPath = Path.Combine(tmp, "pen-red-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovPentest:EnforceOnStartup"] = "true",
                ["SovPentest:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovPentestStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("retestRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void Startup_fails_when_purple_detection_not_required()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov006-pd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovPentestMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-pentest", "pen-red-matrix.json")))!;
            matrix.PurpleDetection!.SocDetectionRequired = false;
            var matrixPath = Path.Combine(tmp, "pen-red-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovPentest:EnforceOnStartup"] = "true",
                ["SovPentest:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovPentestStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("socDetectionRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void LivePenClaim_fails_until_live_assessment()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["SovPentest:EnforceOnStartup"] = "true",
            ["SovPentest:LivePenClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<SovPentestStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LivePenClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix_and_live_Blocked()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["SovPentest:EnforceOnStartup"] = "true",
            ["SovPentest:LivePenClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<SovPentestStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var provider = sp.GetRequiredService<ISovPentestProvider>();
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-independent-pen-assessment" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "NAT-001" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "NAT-002" && b.Status == "NotStarted");

        var guard = sp.GetRequiredService<IPenClaimGuard>();
        Assert.Null(guard.EnsureLivePenAllowed());
        Assert.NotNull(guard.ValidateIndependence(false));
        Assert.NotNull(guard.ValidateFindingClosure(true, false, true));
        Assert.NotNull(guard.ValidatePurpleDetection(false, true));
        Assert.Null(guard.ValidateIndependence(true));
        Assert.Null(guard.ValidateFindingClosure(false, false, true));
    }

    [Fact]
    public void Sov005_accepts_SOV006_Satisfied_without_raising_LiveThreatModel()
    {
        using var sov5 = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "sov-threat", "threat-model-matrix.json")));
        var s5 = sov5.RootElement.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", s5["SOV-006"]);
        Assert.Equal("NotStarted", s5["NAT-001"]);

        var failures = SovThreatEvaluator.EvaluateBlockers(
            JsonSerializer.Deserialize<SovThreatMatrixDocument>(
                System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "sov-threat", "threat-model-matrix.json")))!);
        Assert.DoesNotContain(failures, f => f.Contains("SOV-006", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "sov006-pen-red-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-SOV-006-production-pen-red-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_006_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_006_INDEPENDENT_REVIEW_WAIVER.md")));
        var backlog = System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "completion-backlog", "08-NATIONAL-ASSURANCE-OPERATIONS.md"));
        Assert.Contains("Done (production pen-red binding)", backlog, StringComparison.Ordinal);
    }
}
