using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.SovBaseline;
using HRManagement.ServiceDefaults.SovData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Sov002ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "sov-data", "data-sovereignty-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["SovData:EnforceOnStartup"] = "true",
            ["SovData:LiveSovereigntyClaim"] = "false",
            ["SovData:MatrixPath"] = Path.Combine(RepoRoot, "docs", "sov-data", "data-sovereignty-matrix.json")
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
        services.AddSovData(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["SovData:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<ISovDataProvider>();
        Assert.Equal("SOV-002", provider.Matrix.Card);
        Assert.True(provider.Matrix.Egress?.DenyByDefault == true);
        Assert.True(provider.Matrix.RemoteSupport?.JitRequired == true);
        Assert.True(provider.Matrix.Destruction?.CertificateRequired == true);
        Assert.True(provider.Matrix.MockSandbox?.LiveEgressForbidden == true);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "GOV-001" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SOV-001" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-egress-enforcement" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SYR-008" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SOV-003" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "NAT-001" && b.Status == "NotStarted");
    }

    [Fact]
    public void Startup_fails_when_classification_not_required()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov002-cls-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovDataMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-data", "data-sovereignty-matrix.json")))!;
            matrix.DataCopyInventory!.ClassificationRequired = false;
            var matrixPath = Path.Combine(tmp, "data-sovereignty-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovData:EnforceOnStartup"] = "true",
                ["SovData:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovDataStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("classificationRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_egress_allow_default()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov002-eg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovDataMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-data", "data-sovereignty-matrix.json")))!;
            matrix.Egress!.DenyByDefault = false;
            var matrixPath = Path.Combine(tmp, "data-sovereignty-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovData:EnforceOnStartup"] = "true",
                ["SovData:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovDataStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("denyByDefault", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_jit_not_required()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov002-jit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovDataMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-data", "data-sovereignty-matrix.json")))!;
            matrix.RemoteSupport!.JitRequired = false;
            var matrixPath = Path.Combine(tmp, "data-sovereignty-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovData:EnforceOnStartup"] = "true",
                ["SovData:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovDataStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("jitRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_deletion_cert_not_required()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov002-del-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovDataMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-data", "data-sovereignty-matrix.json")))!;
            matrix.Destruction!.CertificateRequired = false;
            var matrixPath = Path.Combine(tmp, "data-sovereignty-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovData:EnforceOnStartup"] = "true",
                ["SovData:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovDataStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("certificateRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LiveSovereigntyClaim_fails_until_egress_workers()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["SovData:EnforceOnStartup"] = "true",
            ["SovData:LiveSovereigntyClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<SovDataStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveSovereigntyClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix_and_SYR_NotStarted()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["SovData:EnforceOnStartup"] = "true",
            ["SovData:LiveSovereigntyClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<SovDataStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var provider = sp.GetRequiredService<ISovDataProvider>();
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SYR-008" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SOV-003" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "NAT-001" && b.Status == "NotStarted");

        var guard = sp.GetRequiredService<ISovereigntyClaimGuard>();
        Assert.Null(guard.EnsureLiveSovereigntyAllowed());
        Assert.NotNull(guard.ValidateCopyClassification(false, true));
        Assert.NotNull(guard.ValidateEgress(false));
        Assert.NotNull(guard.ValidateRemoteSupport(false, true, true));
        Assert.NotNull(guard.ValidateDestruction(false));
        Assert.Null(guard.ValidateCopyClassification(true, true));
        Assert.Null(guard.ValidateEgress(true));
    }

    [Fact]
    public void Sov001_matrix_accepts_SOV002_Satisfied()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "sov-baseline", "control-baseline-matrix.json")));
        var blockers = doc.RootElement.GetProperty("blockers").EnumerateArray()
            .ToDictionary(
                b => b.GetProperty("id").GetString()!,
                b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["SOV-002"]);
        Assert.Equal("NotStarted", blockers["SYR-001"]);
        Assert.Equal("NotStarted", blockers["NAT-001"]);

        var failures = SovBaselineEvaluator.EvaluateBlockers(
            JsonSerializer.Deserialize<SovBaselineMatrixDocument>(
                System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "sov-baseline", "control-baseline-matrix.json")))!);
        Assert.DoesNotContain(failures, f => f.Contains("SOV-002", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "sov002-data-sovereignty-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-SOV-002-production-data-sovereignty-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_002_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_002_INDEPENDENT_REVIEW_WAIVER.md")));
        var backlog = System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "completion-backlog", "08-NATIONAL-ASSURANCE-OPERATIONS.md"));
        Assert.Contains("Done (production data-sovereignty binding)", backlog, StringComparison.Ordinal);
    }
}
