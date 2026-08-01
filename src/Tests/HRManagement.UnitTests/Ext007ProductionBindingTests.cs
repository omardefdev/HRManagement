using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.ExtSector;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Ext007ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "ext-sector", "sector-catalog-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ExtSector:EnforceOnStartup"] = "true",
            ["ExtSector:LiveSectorCatalogClaim"] = "false",
            ["ExtSector:MatrixPath"] = Path.Combine(RepoRoot, "docs", "ext-sector", "sector-catalog-matrix.json")
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
        services.AddExtSector(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["ExtSector:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IExtSectorProvider>();
        Assert.Equal("EXT-007", provider.Matrix.Card);
        Assert.True(provider.Matrix.NoImplicitCriticalIntegrations);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-sector-adapters" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-notification-channel" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "adapter-fleet-certified-live" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "PROD-005" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "COM-001" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "EXT-005" && b.Status == "Satisfied");
        Assert.True(provider.Matrix.MockSandbox?.LiveAdaptersForbidden == true);
    }

    [Fact]
    public void Startup_fails_when_implicit_critical_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext007-implicit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtSectorMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-sector", "sector-catalog-matrix.json")))!;
            matrix.NoImplicitCriticalIntegrations = false;
            var matrixPath = Path.Combine(tmp, "sector-catalog-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtSector:EnforceOnStartup"] = "true",
                ["ExtSector:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtSectorStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("noImplicitCriticalIntegrations", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_live_adapters_not_forbidden()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext007-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtSectorMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-sector", "sector-catalog-matrix.json")))!;
            matrix.MockSandbox!.LiveAdaptersForbidden = false;
            var matrixPath = Path.Combine(tmp, "sector-catalog-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtSector:EnforceOnStartup"] = "true",
                ["ExtSector:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtSectorStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("liveAdaptersForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_notify_pii_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext007-pii-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtSectorMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-sector", "sector-catalog-matrix.json")))!;
            matrix.Notification!.SensitivePiiForbidden = false;
            var matrixPath = Path.Combine(tmp, "sector-catalog-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtSector:EnforceOnStartup"] = "true",
                ["ExtSector:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtSectorStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("sensitivePiiForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LiveSectorCatalogClaim_fails_until_workers()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["ExtSector:EnforceOnStartup"] = "true",
            ["ExtSector:LiveSectorCatalogClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ExtSectorStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveSectorCatalogClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["ExtSector:EnforceOnStartup"] = "true",
            ["ExtSector:LiveSectorCatalogClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ExtSectorStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var guard = sp.GetRequiredService<ISectorCatalogClaimGuard>();
        Assert.Null(guard.EnsureLiveCatalogAllowed());
        Assert.NotNull(guard.ValidateNoImplicitCritical(true, false));
        Assert.NotNull(guard.ValidateAdapterCompleteness(false, true, true, true, true));
        Assert.NotNull(guard.ValidateNotificationNoSensitivePii(true));
        Assert.NotNull(guard.ValidateFailureSurfaced(true, true));
        Assert.NotNull(guard.ValidateCertification(true, false));
        Assert.NotNull(guard.ValidateReconciliation(true, false));
        Assert.NotNull(guard.ValidateDeliveryReceipt(true, false));
    }

    [Fact]
    public void Ext006_matrix_marks_EXT007_Satisfied()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "ext-sandbox", "sandbox-cert-matrix.json")));
        var blockers = doc.RootElement.GetProperty("blockers").EnumerateArray()
            .ToDictionary(
                b => b.GetProperty("id").GetString()!,
                b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", blockers["EXT-007"]);
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "ext007-sector-catalog-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-EXT-007-production-sector-catalog-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_007_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_007_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
