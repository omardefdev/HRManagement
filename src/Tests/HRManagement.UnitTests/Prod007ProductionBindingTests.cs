using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.ExtApi;
using HRManagement.ServiceDefaults.ProdExtensibility;
using HRManagement.ServiceDefaults.ProdSemantic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Prod007ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "prod-extensibility", "extensibility-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ProdExtensibility:EnforceOnStartup"] = "true",
            ["ProdExtensibility:LiveExtensibilityClaim"] = "false",
            ["ProdExtensibility:MatrixPath"] = Path.Combine(RepoRoot, "docs", "prod-extensibility", "extensibility-matrix.json")
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
        services.AddProdExtensibility(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["ProdExtensibility:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IProdExtensibilityProvider>();
        Assert.Equal("PROD-007", provider.Matrix.Card);
        Assert.True(provider.Matrix.CompatibilityWindows?.SupportNMinus2 == true);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "EXT-006" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "IOP-001" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "REL-004" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-extension-runtime" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "INT-002" && b.Status == "Satisfied");
        Assert.True(provider.Matrix.NoDirectDb);
        Assert.True(provider.Matrix.MockSandbox?.LiveExtensionRuntimeForbidden == true);
    }

    [Fact]
    public void Startup_fails_when_n2_not_required()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod007-n2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdExtensibilityMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-extensibility", "extensibility-matrix.json")))!;
            matrix.CompatibilityWindows!.SupportNMinus2 = false;
            var matrixPath = Path.Combine(tmp, "extensibility-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdExtensibility:EnforceOnStartup"] = "true",
                ["ProdExtensibility:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdExtensibilityStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("supportNMinus2", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_cross_tenant_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod007-ct-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdExtensibilityMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-extensibility", "extensibility-matrix.json")))!;
            matrix.PluginIsolation!.CrossTenantForbidden = false;
            var matrixPath = Path.Combine(tmp, "extensibility-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdExtensibility:EnforceOnStartup"] = "true",
                ["ProdExtensibility:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdExtensibilityStartupValidator>().Single();
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
    public void Startup_fails_when_direct_db_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod007-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdExtensibilityMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-extensibility", "extensibility-matrix.json")))!;
            matrix.NoDirectDb = false;
            var matrixPath = Path.Combine(tmp, "extensibility-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdExtensibility:EnforceOnStartup"] = "true",
                ["ProdExtensibility:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdExtensibilityStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("noDirectDb", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_deprecation_notice_too_short()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod007-dep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdExtensibilityMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-extensibility", "extensibility-matrix.json")))!;
            matrix.Deprecation!.NoticeDaysMin = 30;
            var matrixPath = Path.Combine(tmp, "extensibility-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdExtensibility:EnforceOnStartup"] = "true",
                ["ProdExtensibility:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdExtensibilityStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("noticeDaysMin", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_live_runtime_not_forbidden()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod007-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdExtensibilityMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-extensibility", "extensibility-matrix.json")))!;
            matrix.MockSandbox!.LiveExtensionRuntimeForbidden = false;
            var matrixPath = Path.Combine(tmp, "extensibility-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdExtensibility:EnforceOnStartup"] = "true",
                ["ProdExtensibility:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdExtensibilityStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("liveExtensionRuntimeForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LiveExtensibilityClaim_fails_until_workers()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["ProdExtensibility:EnforceOnStartup"] = "true",
            ["ProdExtensibility:LiveExtensibilityClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ProdExtensibilityStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveExtensibilityClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix_and_EXT006_NotStarted()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["ProdExtensibility:EnforceOnStartup"] = "true",
            ["ProdExtensibility:LiveExtensibilityClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ProdExtensibilityStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var provider = sp.GetRequiredService<IProdExtensibilityProvider>();
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "EXT-006" && b.Status == "NotStarted");
        var guard = sp.GetRequiredService<IExtensibilityClaimGuard>();
        Assert.Null(guard.EnsureLiveExtensibilityAllowed());
        Assert.NotNull(guard.ValidateN2Compatibility(false));
        Assert.NotNull(guard.ValidatePluginIsolation(false, true));
        Assert.NotNull(guard.ValidateNoDirectDb(true));
        Assert.NotNull(guard.ValidateDeprecation(true, false));
        Assert.NotNull(guard.ValidateUpgradePath(false));
    }

    [Fact]
    public void Ext005_and_Prod006_accept_PROD007_Satisfied()
    {
        using var ext005 = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "ext-api", "api-exchange-matrix.json")));
        var e5 = ext005.RootElement.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", e5["PROD-007"]);

        using var prod006 = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "prod-semantic", "semantic-layer-matrix.json")));
        var p6 = prod006.RootElement.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", p6["PROD-007"]);
        Assert.Equal("Blocked", p6["live-open-data-feed"]);

        var extFailures = ExtApiEvaluator.EvaluateBlockers(
            JsonSerializer.Deserialize<ExtApiMatrixDocument>(
                System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "ext-api", "api-exchange-matrix.json")))!);
        Assert.DoesNotContain(extFailures, f => f.Contains("PROD-007", StringComparison.OrdinalIgnoreCase));

        var semFailures = ProdSemanticEvaluator.EvaluateBlockers(
            JsonSerializer.Deserialize<ProdSemanticMatrixDocument>(
                System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "prod-semantic", "semantic-layer-matrix.json")))!);
        Assert.DoesNotContain(semFailures, f => f.Contains("PROD-007", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "prod007-extensibility-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-PROD-007-production-extensibility-platform-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_007_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_007_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
