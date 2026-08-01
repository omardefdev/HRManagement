using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.ArcCriticality;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Arc001ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "arc-criticality", "criticality-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ArcCriticality:EnforceOnStartup"] = "true",
            ["ArcCriticality:LiveCriticalityClaim"] = "false",
            ["ArcCriticality:MatrixPath"] = Path.Combine(RepoRoot, "docs", "arc-criticality", "criticality-matrix.json")
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
        services.AddArcCriticality(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["ArcCriticality:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IArcCriticalityProvider>();
        Assert.Equal("ARC-001", provider.Matrix.Card);
        Assert.True(provider.Matrix.TierClassification?.AllServicesClassifiedRequired == true);
        Assert.True(provider.Matrix.SpofPolicy?.NoUnacceptableSpofInTier01Required == true);
        Assert.True(provider.Matrix.NumericTargetsTelemetry?.EveryNumericTargetHasTelemetryRequired == true);
        Assert.True(provider.Matrix.MockSandbox?.LiveArchitectureFailureWalkthroughForbidden == true);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-architecture-failure-walkthrough" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "ARC-002" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "PHY-001" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SOV-007" && b.Status == "Satisfied");
    }

    [Fact]
    public void Startup_fails_when_services_unclassified_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "arc001-uc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ArcCriticalityMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "arc-criticality", "criticality-matrix.json")))!;
            matrix.TierClassification!.AllServicesClassifiedRequired = false;
            var matrixPath = Path.Combine(tmp, "criticality-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ArcCriticality:EnforceOnStartup"] = "true",
                ["ArcCriticality:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ArcCriticalityStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("allServicesClassifiedRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void Startup_fails_when_tier01_spof_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "arc001-sp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ArcCriticalityMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "arc-criticality", "criticality-matrix.json")))!;
            matrix.SpofPolicy!.NoUnacceptableSpofInTier01Required = false;
            var matrixPath = Path.Combine(tmp, "criticality-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ArcCriticality:EnforceOnStartup"] = "true",
                ["ArcCriticality:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ArcCriticalityStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("noUnacceptableSpofInTier01Required", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void Startup_fails_when_target_without_telemetry_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "arc001-tt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ArcCriticalityMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "arc-criticality", "criticality-matrix.json")))!;
            matrix.NumericTargetsTelemetry!.EveryNumericTargetHasTelemetryRequired = false;
            var matrixPath = Path.Combine(tmp, "criticality-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ArcCriticality:EnforceOnStartup"] = "true",
                ["ArcCriticality:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ArcCriticalityStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("everyNumericTargetHasTelemetryRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void Startup_fails_when_recovery_order_missing()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "arc001-ro-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ArcCriticalityMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "arc-criticality", "criticality-matrix.json")))!;
            matrix.RecoveryOrder!.OrderedStepsRequired = false;
            var matrixPath = Path.Combine(tmp, "criticality-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ArcCriticality:EnforceOnStartup"] = "true",
                ["ArcCriticality:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ArcCriticalityStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("orderedStepsRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void LiveCriticalityClaim_fails_until_live_walkthrough()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["ArcCriticality:EnforceOnStartup"] = "true",
            ["ArcCriticality:LiveCriticalityClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ArcCriticalityStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveCriticalityClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix_and_live_Blocked()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["ArcCriticality:EnforceOnStartup"] = "true",
            ["ArcCriticality:LiveCriticalityClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ArcCriticalityStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var provider = sp.GetRequiredService<IArcCriticalityProvider>();
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-architecture-failure-walkthrough" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "ARC-002" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "PHY-001" && b.Status == "Satisfied");

        var guard = sp.GetRequiredService<ICriticalityClaimGuard>();
        Assert.Null(guard.EnsureLiveCriticalityAllowed());
        Assert.NotNull(guard.ValidateClassification(false, true));
        Assert.NotNull(guard.ValidateSpof(true));
        Assert.NotNull(guard.ValidateTelemetryBinding(true));
        Assert.NotNull(guard.ValidateRecoveryOrder(false, true));
        Assert.Null(guard.ValidateClassification(true, true));
        Assert.Null(guard.ValidateSpof(false));
        Assert.Null(guard.ValidateTelemetryBinding(false));
        Assert.Null(guard.ValidateRecoveryOrder(true, true));
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "arc001-criticality-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-ARC-001-production-criticality-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ARC_001_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ARC_001_INDEPENDENT_REVIEW_WAIVER.md")));
        var backlog = System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "completion-backlog", "08-NATIONAL-ASSURANCE-OPERATIONS.md"));
        Assert.Contains("Done (production criticality binding)", backlog, StringComparison.Ordinal);
    }
}
