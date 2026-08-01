using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.ExtCivil;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Ext002ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "ext-civil", "civil-registry-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ExtCivil:EnforceOnStartup"] = "true",
            ["ExtCivil:LiveMoiClaim"] = "false",
            ["ExtCivil:MatrixPath"] = Path.Combine(RepoRoot, "docs", "ext-civil", "civil-registry-matrix.json")
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
        services.AddExtCivil(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["ExtCivil:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IExtCivilProvider>();
        Assert.Equal("EXT-002", provider.Matrix.Card);
        Assert.True(provider.Matrix.PurposeAllowlist.Count >= 3);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "moi-civil-registry-live" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-sandbox-cert" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "national-idp-live" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "L10N-002" && b.Status == "Done");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "EXT-001" && b.Status == "Satisfied");
        Assert.True(provider.Matrix.MockSandbox?.LiveMoiForbidden == true);
    }

    [Fact]
    public void Startup_fails_when_consent_not_required()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext002-consent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtCivilMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-civil", "civil-registry-matrix.json")))!;
            matrix.ConsentRequired = false;
            var matrixPath = Path.Combine(tmp, "civil-registry-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtCivil:EnforceOnStartup"] = "true",
                ["ExtCivil:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtCivilStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("consentRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_live_moi_not_forbidden()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext002-moi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtCivilMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-civil", "civil-registry-matrix.json")))!;
            matrix.MockSandbox!.LiveMoiForbidden = false;
            var matrixPath = Path.Combine(tmp, "civil-registry-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtCivil:EnforceOnStartup"] = "true",
                ["ExtCivil:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtCivilStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("liveMoiForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_bulk_harvest_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext002-bulk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtCivilMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-civil", "civil-registry-matrix.json")))!;
            matrix.RateLimits!.BulkHarvestForbidden = false;
            var matrixPath = Path.Combine(tmp, "civil-registry-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtCivil:EnforceOnStartup"] = "true",
                ["ExtCivil:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtCivilStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("bulkHarvestForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_unverified_as_truth_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext002-unavail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtCivilMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-civil", "civil-registry-matrix.json")))!;
            matrix.UnavailableMode!.ForbidUnverifiedAsTruth = false;
            var matrixPath = Path.Combine(tmp, "civil-registry-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtCivil:EnforceOnStartup"] = "true",
                ["ExtCivil:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtCivilStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("forbidUnverifiedAsTruth", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LiveMoiClaim_fails_until_workers()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["ExtCivil:EnforceOnStartup"] = "true",
            ["ExtCivil:LiveMoiClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ExtCivilStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveMoiClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["ExtCivil:EnforceOnStartup"] = "true",
            ["ExtCivil:LiveMoiClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ExtCivilStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var guard = sp.GetRequiredService<ICivilRegistryClaimGuard>();
        Assert.Null(guard.EnsureLiveMoiAllowed());
        Assert.NotNull(guard.ValidatePurpose("marketing-enrichment"));
        Assert.NotNull(guard.ValidateOutageAsTruth(registryAvailable: false, treatedAsVerified: true));
        Assert.NotNull(guard.ValidateBulkHarvest(true));
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "ext002-civil-registry-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-EXT-002-production-civil-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_002_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_002_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
