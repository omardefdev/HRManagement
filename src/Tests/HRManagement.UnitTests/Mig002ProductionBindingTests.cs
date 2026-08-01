using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.MigCanonical;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Mig002ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "mig-canonical", "code-map-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["MigCanonical:EnforceOnStartup"] = "true",
            ["MigCanonical:LiveCodeMapEngineClaim"] = "false",
            ["MigCanonical:MatrixPath"] = Path.Combine(RepoRoot, "docs", "mig-canonical", "code-map-matrix.json")
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
        services.AddMigCanonical(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["MigCanonical:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IMigCanonicalProvider>();
        Assert.Equal("MIG-002", provider.Matrix.Card);
        Assert.True(provider.Matrix.CodeMaps.Count >= 10);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "agency-extract-access" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SYR-003" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "MDM-001" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "MIG-001" && b.Status == "Satisfied");
        Assert.True(provider.Matrix.UnmappedPolicy?.ForbidOther == true);
    }

    [Fact]
    public void Startup_fails_on_null_disposition()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mig002-disp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<MigCanonicalMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "mig-canonical", "code-map-matrix.json")))!;
            matrix.CodeMaps[0].Disposition = null;
            var matrixPath = Path.Combine(tmp, "code-map-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["MigCanonical:EnforceOnStartup"] = "true",
                ["MigCanonical:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<MigCanonicalStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("MIG-002", ex.Message, StringComparison.Ordinal);
            Assert.Contains("disposition", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_on_mass_other_target()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mig002-other-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<MigCanonicalMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "mig-canonical", "code-map-matrix.json")))!;
            var map = matrix.CodeMaps.First(m =>
                string.Equals(m.Disposition, "Map", StringComparison.OrdinalIgnoreCase));
            map.TargetValue = "Other";
            var matrixPath = Path.Combine(tmp, "code-map-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["MigCanonical:EnforceOnStartup"] = "true",
                ["MigCanonical:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<MigCanonicalStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("catch-all", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_on_legacy_id_as_auth_key_policy()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mig002-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<MigCanonicalMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "mig-canonical", "code-map-matrix.json")))!;
            matrix.LegacyIdPolicy!.NeverUseAsAuthOrTenantKey = false;
            var matrixPath = Path.Combine(tmp, "code-map-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["MigCanonical:EnforceOnStartup"] = "true",
                ["MigCanonical:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<MigCanonicalStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("neverUseAsAuthOrTenantKey", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LiveCodeMapEngineClaim_fails_until_mig003()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["MigCanonical:EnforceOnStartup"] = "true",
            ["MigCanonical:LiveCodeMapEngineClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<MigCanonicalStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveCodeMapEngineClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["MigCanonical:EnforceOnStartup"] = "true",
            ["MigCanonical:LiveCodeMapEngineClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<MigCanonicalStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var guard = sp.GetRequiredService<ICodeMapClaimGuard>();
        Assert.Null(guard.EnsureLiveCodeMapEngineAllowed());
        Assert.NotNull(guard.ValidateLegacyIdUse("EmpNo"));
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "mig002-codemap-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-MIG-002-production-codemap-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_002_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_002_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
