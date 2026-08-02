using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.ProdPolicy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Prod001ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "prod-policy", "policy-platform-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ProdPolicy:EnforceOnStartup"] = "true",
            ["ProdPolicy:LivePolicyEngineClaim"] = "false",
            ["ProdPolicy:MatrixPath"] = Path.Combine(RepoRoot, "docs", "prod-policy", "policy-platform-matrix.json")
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
        services.AddProdPolicy(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["ProdPolicy:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IProdPolicyProvider>();
        Assert.Equal("PROD-001", provider.Matrix.Card);
        Assert.True(provider.Matrix.InvalidPublishForbidden);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-policy-engine" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-workflow-runtime" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-form-designer" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "PROD-002" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "ONB-001" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "STR-002" && b.Status == "Satisfied");
        Assert.True(provider.Matrix.MockSandbox?.LiveEngineForbidden == true);
    }

    [Fact]
    public void Startup_fails_when_invalid_publish_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod001-inv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdPolicyMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-policy", "policy-platform-matrix.json")))!;
            matrix.InvalidPublishForbidden = false;
            var matrixPath = Path.Combine(tmp, "policy-platform-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdPolicy:EnforceOnStartup"] = "true",
                ["ProdPolicy:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdPolicyStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("invalidPublishForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_live_engine_not_forbidden()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod001-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdPolicyMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-policy", "policy-platform-matrix.json")))!;
            matrix.MockSandbox!.LiveEngineForbidden = false;
            var matrixPath = Path.Combine(tmp, "policy-platform-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdPolicy:EnforceOnStartup"] = "true",
                ["ProdPolicy:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdPolicyStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("liveEngineForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_override_may_bypass_national()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod001-ov-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdPolicyMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-policy", "policy-platform-matrix.json")))!;
            matrix.TenantOverride!.CannotBypassNationalMandatory = false;
            var matrixPath = Path.Combine(tmp, "policy-platform-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdPolicy:EnforceOnStartup"] = "true",
                ["ProdPolicy:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdPolicyStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("cannotBypassNationalMandatory", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_real_data_sim_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod001-sim-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdPolicyMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-policy", "policy-platform-matrix.json")))!;
            matrix.Simulation!.RealDataForbidden = false;
            var matrixPath = Path.Combine(tmp, "policy-platform-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdPolicy:EnforceOnStartup"] = "true",
                ["ProdPolicy:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdPolicyStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("realDataForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LivePolicyEngineClaim_fails_until_workers()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["ProdPolicy:EnforceOnStartup"] = "true",
            ["ProdPolicy:LivePolicyEngineClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ProdPolicyStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LivePolicyEngineClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["ProdPolicy:EnforceOnStartup"] = "true",
            ["ProdPolicy:LivePolicyEngineClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ProdPolicyStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var guard = sp.GetRequiredService<IPolicyPlatformClaimGuard>();
        Assert.Null(guard.EnsureLiveEngineAllowed());
        Assert.NotNull(guard.ValidateVersionedTemplate(false, "policy"));
        Assert.NotNull(guard.ValidateHistoricalPin("2025-01-01", false));
        Assert.NotNull(guard.ValidateFourEyesPublish("same", "same"));
        Assert.NotNull(guard.ValidateSimulationSynthetic(true));
        Assert.NotNull(guard.ValidateInvalidPublish(false, true));
        Assert.NotNull(guard.ValidateOverrideCeiling(true));
        Assert.NotNull(guard.ValidateNoArbitraryCode(true));
        Assert.NotNull(guard.ValidateRollback(false));
    }

    [Fact]
    public void Str004_matrix_marks_PROD001_Satisfied()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "str-product", "nofork-matrix.json")));
        var blockers = doc.RootElement.GetProperty("blockers").EnumerateArray()
            .ToDictionary(
                b => b.GetProperty("id").GetString()!,
                b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["PROD-001"]);
        Assert.Equal("Satisfied", blockers["ONB-001"]);
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "prod001-policy-platform-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-PROD-001-production-policy-platform-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_001_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_001_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
