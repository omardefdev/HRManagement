using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.SovBaseline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Sov001ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "sov-baseline", "control-baseline-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["SovBaseline:EnforceOnStartup"] = "true",
            ["SovBaseline:LiveAtoClaim"] = "false",
            ["SovBaseline:MatrixPath"] = Path.Combine(RepoRoot, "docs", "sov-baseline", "control-baseline-matrix.json")
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
        services.AddSovBaseline(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["SovBaseline:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<ISovBaselineProvider>();
        Assert.Equal("SOV-001", provider.Matrix.Card);
        Assert.True(provider.Matrix.ControlCoverage?.FullCoverageRequired == true);
        Assert.True(provider.Matrix.OwnershipEvidence?.OwnerRequired == true);
        Assert.True(provider.Matrix.Poam?.DatedRemediationRequired == true);
        Assert.True(provider.Matrix.IndependentAssessment?.LiveAtoForbidden == true);
        Assert.True(provider.Matrix.MockSandbox?.LiveAtoForbidden == true);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "STR-002" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-independent-ato" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SYR-001" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SOV-002" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "NAT-001" && b.Status == "NotStarted");
    }

    [Fact]
    public void Startup_fails_when_owner_required_false()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov001-owner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovBaselineMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-baseline", "control-baseline-matrix.json")))!;
            matrix.OwnershipEvidence!.OwnerRequired = false;
            var matrixPath = Path.Combine(tmp, "control-baseline-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovBaseline:EnforceOnStartup"] = "true",
                ["SovBaseline:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovBaselineStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("ownerRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_evidence_required_false()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov001-ev-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovBaselineMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-baseline", "control-baseline-matrix.json")))!;
            matrix.OwnershipEvidence!.EvidenceRequired = false;
            var matrixPath = Path.Combine(tmp, "control-baseline-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovBaseline:EnforceOnStartup"] = "true",
                ["SovBaseline:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovBaselineStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("evidenceRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_coverage_gap()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov001-cov-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovBaselineMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-baseline", "control-baseline-matrix.json")))!;
            matrix.ControlCoverage!.FullCoverageRequired = false;
            var matrixPath = Path.Combine(tmp, "control-baseline-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovBaseline:EnforceOnStartup"] = "true",
                ["SovBaseline:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovBaselineStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("fullCoverageRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_undated_poam()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov001-poam-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovBaselineMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-baseline", "control-baseline-matrix.json")))!;
            matrix.Poam!.DatedRemediationRequired = false;
            var matrixPath = Path.Combine(tmp, "control-baseline-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovBaseline:EnforceOnStartup"] = "true",
                ["SovBaseline:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovBaselineStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("datedRemediationRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LiveAtoClaim_fails_until_independent_ato()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["SovBaseline:EnforceOnStartup"] = "true",
            ["SovBaseline:LiveAtoClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<SovBaselineStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveAtoClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix_and_SYR_NotStarted()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["SovBaseline:EnforceOnStartup"] = "true",
            ["SovBaseline:LiveAtoClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<SovBaselineStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var provider = sp.GetRequiredService<ISovBaselineProvider>();
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SYR-001" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SOV-002" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "NAT-001" && b.Status == "NotStarted");

        var guard = sp.GetRequiredService<IAtoClaimGuard>();
        Assert.Null(guard.EnsureLiveAtoAllowed());
        Assert.NotNull(guard.ValidateControlOwnership(false, true));
        Assert.NotNull(guard.ValidateControlOwnership(true, false));
        Assert.NotNull(guard.ValidateCoverage(false, true));
        Assert.NotNull(guard.ValidateCoverage(true, false));
        Assert.NotNull(guard.ValidatePoam(false));
        Assert.Null(guard.ValidatePoam(true));
        Assert.Null(guard.ValidateControlOwnership(true, true));
    }

    [Fact]
    public void Str002_catalog_accepts_SOV001_Satisfied_and_keeps_Blocked_Legal()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "str-legal", "control-catalog.json")));
        var root = doc.RootElement;
        var blockers = root.GetProperty("blockers").EnumerateArray()
            .ToDictionary(
                b => b.GetProperty("id").GetString()!,
                b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["SOV-001"]);
        Assert.Equal("Blocked", blockers["SYR-001"]);

        var blockedLegal = root.GetProperty("controls").EnumerateArray()
            .Where(c => string.Equals(c.GetProperty("status").GetString(), "Blocked-Legal", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(blockedLegal.Count >= 1, "CTRL-* Blocked-Legal must remain (not flipped by SOV-001 binding)");

        var sovHosting = root.GetProperty("controls").EnumerateArray()
            .FirstOrDefault(c => string.Equals(
                c.GetProperty("requirementId").GetString(), "CTRL-SOV-HOSTING", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(JsonValueKind.Object, sovHosting.ValueKind);
        Assert.Equal("Blocked-Legal", sovHosting.GetProperty("status").GetString());
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "sov001-control-baseline-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-SOV-001-production-control-baseline-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_001_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_001_INDEPENDENT_REVIEW_WAIVER.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "completion-backlog", "08-NATIONAL-ASSURANCE-OPERATIONS.md")));
        var backlog = System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "completion-backlog", "08-NATIONAL-ASSURANCE-OPERATIONS.md"));
        Assert.Contains("Done (production control-baseline binding)", backlog, StringComparison.Ordinal);
    }
}
