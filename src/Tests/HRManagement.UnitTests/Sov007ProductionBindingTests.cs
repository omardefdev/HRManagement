using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.Ownership;
using HRManagement.ServiceDefaults.SovVendor;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Sov007ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "sov-vendor", "vendor-exit-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["SovVendor:EnforceOnStartup"] = "true",
            ["SovVendor:LiveVendorClaim"] = "false",
            ["SovVendor:MatrixPath"] = Path.Combine(RepoRoot, "docs", "sov-vendor", "vendor-exit-matrix.json")
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
        services.AddSovVendor(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["SovVendor:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<ISovVendorProvider>();
        Assert.Equal("SOV-007", provider.Matrix.Card);
        Assert.True(provider.Matrix.Ownership?.IpSourceBuildIacSbomDocsRequired == true);
        Assert.True(provider.Matrix.OpenExport?.FullDataExportRequired == true);
        Assert.True(provider.Matrix.CleanRoomRebuild?.OwnedArtifactsOnlyRequired == true);
        Assert.True(provider.Matrix.MockSandbox?.LiveCleanRoomRebuildForbidden == true);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-clean-room-rebuild" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-vendor-exit-drill" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "VEN-001" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "NAT-001" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SOV-006" && b.Status == "Satisfied");
    }

    [Fact]
    public void Startup_fails_when_rebuild_unowned_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov007-ru-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovVendorMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-vendor", "vendor-exit-matrix.json")))!;
            matrix.CleanRoomRebuild!.OwnedArtifactsOnlyRequired = false;
            var matrixPath = Path.Combine(tmp, "vendor-exit-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovVendor:EnforceOnStartup"] = "true",
                ["SovVendor:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovVendorStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("ownedArtifactsOnlyRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void Startup_fails_when_incomplete_export_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov007-ie-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovVendorMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-vendor", "vendor-exit-matrix.json")))!;
            matrix.OpenExport!.FullDataExportRequired = false;
            var matrixPath = Path.Combine(tmp, "vendor-exit-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovVendor:EnforceOnStartup"] = "true",
                ["SovVendor:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovVendorStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("fullDataExportRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void Startup_fails_when_critical_no_alternate_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov007-cn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovVendorMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-vendor", "vendor-exit-matrix.json")))!;
            matrix.CriticalAlternates!.NoCriticalWithoutAlternateOrException = false;
            var matrixPath = Path.Combine(tmp, "vendor-exit-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovVendor:EnforceOnStartup"] = "true",
                ["SovVendor:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovVendorStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("noCriticalWithoutAlternateOrException", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void Startup_fails_when_destruction_unattested_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov007-du-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovVendorMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-vendor", "vendor-exit-matrix.json")))!;
            matrix.DestructionAttestation!.PriorVendorProofRequired = false;
            var matrixPath = Path.Combine(tmp, "vendor-exit-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovVendor:EnforceOnStartup"] = "true",
                ["SovVendor:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovVendorStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("priorVendorProofRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void LiveVendorClaim_fails_until_live_clean_room()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["SovVendor:EnforceOnStartup"] = "true",
            ["SovVendor:LiveVendorClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<SovVendorStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveVendorClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix_and_live_Blocked()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["SovVendor:EnforceOnStartup"] = "true",
            ["SovVendor:LiveVendorClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<SovVendorStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var provider = sp.GetRequiredService<ISovVendorProvider>();
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-clean-room-rebuild" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "VEN-001" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "NAT-001" && b.Status == "NotStarted");

        var guard = sp.GetRequiredService<IVendorClaimGuard>();
        Assert.Null(guard.EnsureLiveVendorAllowed());
        Assert.NotNull(guard.ValidateCleanRoom(false, true));
        Assert.NotNull(guard.ValidateOpenExport(false, true));
        Assert.NotNull(guard.ValidateCriticalAlternates(true));
        Assert.NotNull(guard.ValidateDestruction(false));
        Assert.Null(guard.ValidateCleanRoom(true, true));
        Assert.Null(guard.ValidateOpenExport(true, true));
        Assert.Null(guard.ValidateCriticalAlternates(false));
        Assert.Null(guard.ValidateDestruction(true));
    }

    [Fact]
    public void Str006_accepts_SOV007_Satisfied_without_raising_National24x7()
    {
        using var str6 = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "str-ownership", "ownership-matrix.json")));
        var s6 = str6.RootElement.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", s6["SOV-007"]);
        Assert.Equal("Blocked", s6["SRE-001"]);
        Assert.Equal("NotStarted", s6["FIN-001"]);

        var matrix = JsonSerializer.Deserialize<OwnershipMatrixDocument>(
            System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "str-ownership", "ownership-matrix.json")))!;
        var failures = OwnershipEvaluator.EvaluateBlockers(matrix);
        Assert.DoesNotContain(failures, f => f.Contains("SOV-007", StringComparison.OrdinalIgnoreCase));
        Assert.False(matrix.Staffing?.National24x7Claim == true);
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "sov007-vendor-exit-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-SOV-007-production-vendor-exit-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_007_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_007_INDEPENDENT_REVIEW_WAIVER.md")));
        var backlog = System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "completion-backlog", "08-NATIONAL-ASSURANCE-OPERATIONS.md"));
        Assert.Contains("Done (production vendor-exit binding)", backlog, StringComparison.Ordinal);
    }
}
