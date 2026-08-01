using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.MigInventory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Mig001ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "mig-inventory", "source-inventory.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["MigInventory:EnforceOnStartup"] = "true",
            ["MigInventory:LiveAgencyExtractClaim"] = "false",
            ["MigInventory:MatrixPath"] = Path.Combine(RepoRoot, "docs", "mig-inventory", "source-inventory.json")
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
        services.AddMigInventory(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["MigInventory:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IMigInventoryProvider>();
        Assert.Equal("MIG-001", provider.Matrix.Card);
        Assert.True(provider.Matrix.Sources.Count >= 6);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "agency-extract-access" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SYR-002" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "STR-001" && b.Status == "Satisfied");
        Assert.NotEmpty(provider.Matrix.TargetFieldSot);
    }

    [Fact]
    public void Startup_fails_on_inventoried_estimate_only()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mig001-est-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<MigInventoryMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "mig-inventory", "source-inventory.json")))!;
            var inventoried = matrix.Sources.First(s =>
                string.Equals(s.Status, "Inventoried", StringComparison.OrdinalIgnoreCase));
            inventoried.Volume ??= new MigSourceVolume();
            inventoried.Volume.EstimateOnly = true;
            var matrixPath = Path.Combine(tmp, "source-inventory.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["MigInventory:EnforceOnStartup"] = "true",
                ["MigInventory:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<MigInventoryStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("MIG-001", ex.Message, StringComparison.Ordinal);
            Assert.Contains("estimateOnly", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_on_missing_required_sot()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mig001-sot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<MigInventoryMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "mig-inventory", "source-inventory.json")))!;
            matrix.TargetFieldSot.RemoveAll(t =>
                string.Equals(t.TargetField, matrix.RequiredTargetFields[0], StringComparison.OrdinalIgnoreCase));
            var matrixPath = Path.Combine(tmp, "source-inventory.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["MigInventory:EnforceOnStartup"] = "true",
                ["MigInventory:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<MigInventoryStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("SoT", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LiveAgencyExtractClaim_fails_while_access_blocked()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["MigInventory:EnforceOnStartup"] = "true",
            ["MigInventory:LiveAgencyExtractClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<MigInventoryStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveAgencyExtractClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["MigInventory:EnforceOnStartup"] = "true",
            ["MigInventory:LiveAgencyExtractClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<MigInventoryStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var guard = sp.GetRequiredService<ILegacyExtractClaimGuard>();
        Assert.Null(guard.EnsureLiveAgencyExtractAllowed());
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "mig001-inventory-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-MIG-001-production-inventory-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_001_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_001_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
