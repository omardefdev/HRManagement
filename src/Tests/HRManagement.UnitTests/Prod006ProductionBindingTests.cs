using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.ProdSemantic;
using HRManagement.ServiceDefaults.ProdWorkbox;
using HRManagement.ServiceDefaults.ValueKpi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Prod006ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "prod-semantic", "semantic-layer-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ProdSemantic:EnforceOnStartup"] = "true",
            ["ProdSemantic:LiveSemanticLayerClaim"] = "false",
            ["ProdSemantic:MatrixPath"] = Path.Combine(RepoRoot, "docs", "prod-semantic", "semantic-layer-matrix.json")
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
        services.AddProdSemantic(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["ProdSemantic:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IProdSemanticProvider>();
        Assert.Equal("PROD-006", provider.Matrix.Card);
        Assert.True(provider.Matrix.SemanticMetrics?.UnifiedDefinitionRequired == true);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "ANA-001" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "REP-001" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "PROD-007" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-warehouse" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-bi-dashboard" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "GOV-003" && b.Status == "Satisfied");
        Assert.True(provider.Matrix.MockSandbox?.LiveBiDashboardForbidden == true);
    }

    [Fact]
    public void Startup_fails_when_unified_definition_not_required()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod006-kpi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdSemanticMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-semantic", "semantic-layer-matrix.json")))!;
            matrix.SemanticMetrics!.UnifiedDefinitionRequired = false;
            var matrixPath = Path.Combine(tmp, "semantic-layer-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdSemantic:EnforceOnStartup"] = "true",
                ["ProdSemantic:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdSemanticStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("unifiedDefinitionRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_cross_tenant_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod006-rls-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdSemanticMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-semantic", "semantic-layer-matrix.json")))!;
            matrix.TenantRegionRollups!.CrossTenantForbidden = false;
            var matrixPath = Path.Combine(tmp, "semantic-layer-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdSemantic:EnforceOnStartup"] = "true",
                ["ProdSemantic:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdSemanticStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("crossTenantForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_small_cell_not_required()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod006-priv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdSemanticMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-semantic", "semantic-layer-matrix.json")))!;
            matrix.PrivacyThresholds!.SmallCellSuppressionRequired = false;
            var matrixPath = Path.Combine(tmp, "semantic-layer-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdSemantic:EnforceOnStartup"] = "true",
                ["ProdSemantic:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdSemanticStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("smallCellSuppressionRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_provenance_not_required()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod006-prov-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdSemanticMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-semantic", "semantic-layer-matrix.json")))!;
            matrix.Lineage!.VersionAsOfSourceRequired = false;
            var matrixPath = Path.Combine(tmp, "semantic-layer-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdSemantic:EnforceOnStartup"] = "true",
                ["ProdSemantic:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdSemanticStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("versionAsOfSourceRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_live_bi_not_forbidden()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod006-bi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdSemanticMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-semantic", "semantic-layer-matrix.json")))!;
            matrix.MockSandbox!.LiveBiDashboardForbidden = false;
            var matrixPath = Path.Combine(tmp, "semantic-layer-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdSemantic:EnforceOnStartup"] = "true",
                ["ProdSemantic:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdSemanticStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("liveBiDashboardForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LiveSemanticLayerClaim_fails_until_workers()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["ProdSemantic:EnforceOnStartup"] = "true",
            ["ProdSemantic:LiveSemanticLayerClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ProdSemanticStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveSemanticLayerClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix_and_ANA001_NotStarted()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["ProdSemantic:EnforceOnStartup"] = "true",
            ["ProdSemantic:LiveSemanticLayerClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ProdSemanticStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var provider = sp.GetRequiredService<IProdSemanticProvider>();
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "ANA-001" && b.Status == "NotStarted");
        var guard = sp.GetRequiredService<ISemanticLayerClaimGuard>();
        Assert.Null(guard.EnsureLiveSemanticAllowed());
        Assert.NotNull(guard.ValidateUnifiedKpiDefinition(false));
        Assert.NotNull(guard.ValidateReconciliation(false));
        Assert.NotNull(guard.ValidateTenantRls(false));
        Assert.NotNull(guard.ValidatePrivacyThreshold(false, true));
        Assert.NotNull(guard.ValidateReportProvenance(false, true, true));
        Assert.NotNull(guard.ValidateFreshness(false));
    }

    [Fact]
    public void Prod005_accepts_PROD006_Satisfied_and_Str007_still_blocks_LiveDashboardClaim()
    {
        using var workbox = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "prod-workbox", "workbox-matrix.json")));
        var blockers = workbox.RootElement.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["PROD-006"]);

        var wbFailures = ProdWorkboxEvaluator.EvaluateBlockers(
            JsonSerializer.Deserialize<ProdWorkboxMatrixDocument>(
                System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "prod-workbox", "workbox-matrix.json")))!);
        Assert.DoesNotContain(wbFailures, f => f.Contains("PROD-006", StringComparison.OrdinalIgnoreCase));

        var values = new Dictionary<string, string?>
        {
            ["ProgramValue:EnforceOnStartup"] = "true",
            ["ProgramValue:LiveDashboardClaim"] = "true",
            ["ProgramValue:MatrixPath"] = Path.Combine(RepoRoot, "docs", "str-value", "kpi-matrix.json")
        };
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment
        {
            EnvironmentName = Environments.Production,
            ContentRootPath = RepoRoot,
            ApplicationName = "tests",
            ContentRootFileProvider = new PhysicalFileProvider(RepoRoot)
        });
        services.AddValueKpi(config);
        using var sp = services.BuildServiceProvider();
        var validator = sp.GetServices<IHostedService>().OfType<ValueKpiStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("PROD-006c", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("live-bi-dashboard", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "prod006-semantic-layer-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-PROD-006-production-semantic-layer-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_006_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_006_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
