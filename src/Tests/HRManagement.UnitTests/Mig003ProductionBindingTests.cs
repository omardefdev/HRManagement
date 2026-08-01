using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.MigFactory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Mig003ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "mig-factory", "pipeline-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["MigFactory:EnforceOnStartup"] = "true",
            ["MigFactory:LiveEtlWorkersClaim"] = "false",
            ["MigFactory:MatrixPath"] = Path.Combine(RepoRoot, "docs", "mig-factory", "pipeline-matrix.json")
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
        services.AddMigFactory(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["MigFactory:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IMigFactoryProvider>();
        Assert.Equal("MIG-003", provider.Matrix.Card);
        Assert.True(provider.Matrix.Stages.Count >= 6);
        Assert.True(provider.Matrix.StagingZones.Count >= 3);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "agency-extract-access" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-cutover-perf" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "MIG-002" && b.Status == "Satisfied");
        Assert.True(provider.Matrix.Idempotency?.DuplicateOnReloadForbidden == true);
    }

    [Fact]
    public void Startup_fails_on_unencrypted_zone()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mig003-zone-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<MigFactoryMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "mig-factory", "pipeline-matrix.json")))!;
            matrix.StagingZones[0].EncryptionAtRest = false;
            var matrixPath = Path.Combine(tmp, "pipeline-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["MigFactory:EnforceOnStartup"] = "true",
                ["MigFactory:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<MigFactoryStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("MIG-003", ex.Message, StringComparison.Ordinal);
            Assert.Contains("encryptionAtRest", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_on_weak_idempotency()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mig003-idemp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<MigFactoryMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "mig-factory", "pipeline-matrix.json")))!;
            matrix.Idempotency!.DuplicateOnReloadForbidden = false;
            var matrixPath = Path.Combine(tmp, "pipeline-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["MigFactory:EnforceOnStartup"] = "true",
                ["MigFactory:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<MigFactoryStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("duplicateOnReloadForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_on_missing_lineage_field()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mig003-lin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<MigFactoryMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "mig-factory", "pipeline-matrix.json")))!;
            matrix.Lineage!.PerRecordFields.RemoveAll(f =>
                string.Equals(f, "tenantId", StringComparison.OrdinalIgnoreCase));
            var matrixPath = Path.Combine(tmp, "pipeline-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["MigFactory:EnforceOnStartup"] = "true",
                ["MigFactory:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<MigFactoryStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("tenantId", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_on_empty_pii_policy()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mig003-pii-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<MigFactoryMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "mig-factory", "pipeline-matrix.json")))!;
            matrix.PiiLogging!.ForbiddenFields.Clear();
            var matrixPath = Path.Combine(tmp, "pipeline-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["MigFactory:EnforceOnStartup"] = "true",
                ["MigFactory:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<MigFactoryStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("forbiddenFields", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LiveEtlWorkersClaim_fails_until_workers()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["MigFactory:EnforceOnStartup"] = "true",
            ["MigFactory:LiveEtlWorkersClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<MigFactoryStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveEtlWorkersClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_fails_on_liveProofRequired_while_blocked()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "mig003-proof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<MigFactoryMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "mig-factory", "pipeline-matrix.json")))!;
            matrix.Throughput!.LiveProofRequired = true;
            var matrixPath = Path.Combine(tmp, "pipeline-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["MigFactory:EnforceOnStartup"] = "true",
                ["MigFactory:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<MigFactoryStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("liveProofRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_passes_with_current_matrix()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["MigFactory:EnforceOnStartup"] = "true",
            ["MigFactory:LiveEtlWorkersClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<MigFactoryStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var guard = sp.GetRequiredService<IEtlWorkersClaimGuard>();
        Assert.Null(guard.EnsureLiveEtlWorkersAllowed());
        Assert.NotNull(guard.ValidateThroughputClaim(liveProof: true));
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "mig003-factory-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-MIG-003-production-factory-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_003_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "MIG_003_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
