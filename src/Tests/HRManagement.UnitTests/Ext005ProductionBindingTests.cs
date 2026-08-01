using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.ExtApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Ext005ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "ext-api", "api-exchange-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ExtApi:EnforceOnStartup"] = "true",
            ["ExtApi:LiveExchangeClaim"] = "false",
            ["ExtApi:MatrixPath"] = Path.Combine(RepoRoot, "docs", "ext-api", "api-exchange-matrix.json")
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
        services.AddExtApi(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["ExtApi:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IExtApiProvider>();
        Assert.Equal("EXT-005", provider.Matrix.Card);
        Assert.True(provider.Matrix.CatalogRequired);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "gov-exchange-bus-live" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "schema-registry-live" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "partner-cdc-cert" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "national-openapi-publish" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "PROD-007" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "IAM-004" && b.Status == "Satisfied");
        Assert.True(provider.Matrix.MockSandbox?.LiveBusForbidden == true);
    }

    [Fact]
    public void Startup_fails_when_catalog_not_required()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext005-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtApiMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-api", "api-exchange-matrix.json")))!;
            matrix.CatalogRequired = false;
            var matrixPath = Path.Combine(tmp, "api-exchange-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtApi:EnforceOnStartup"] = "true",
                ["ExtApi:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtApiStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("catalogRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_live_bus_not_forbidden()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext005-bus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtApiMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-api", "api-exchange-matrix.json")))!;
            matrix.MockSandbox!.LiveBusForbidden = false;
            var matrixPath = Path.Combine(tmp, "api-exchange-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtApi:EnforceOnStartup"] = "true",
                ["ExtApi:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtApiStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("liveBusForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_shared_db_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext005-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtApiMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-api", "api-exchange-matrix.json")))!;
            matrix.SharedDbForbidden = false;
            var matrixPath = Path.Combine(tmp, "api-exchange-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtApi:EnforceOnStartup"] = "true",
                ["ExtApi:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtApiStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("sharedDbForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_certified_consumers_unprotected()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext005-cdc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtApiMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-api", "api-exchange-matrix.json")))!;
            matrix.ConsumerDriven!.CertifiedConsumersProtected = false;
            var matrixPath = Path.Combine(tmp, "api-exchange-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtApi:EnforceOnStartup"] = "true",
                ["ExtApi:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtApiStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("certifiedConsumersProtected", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LiveExchangeClaim_fails_until_workers()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["ExtApi:EnforceOnStartup"] = "true",
            ["ExtApi:LiveExchangeClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ExtApiStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveExchangeClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["ExtApi:EnforceOnStartup"] = "true",
            ["ExtApi:LiveExchangeClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ExtApiStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var guard = sp.GetRequiredService<IApiExchangeClaimGuard>();
        Assert.Null(guard.EnsureLiveExchangeAllowed());
        Assert.NotNull(guard.ValidateCatalogEntry(["integrationId", "contractRef"]));
        Assert.NotNull(guard.ValidateBreakingChange(true, false));
        Assert.NotNull(guard.ValidateSharedDb(true));
        Assert.NotNull(guard.ValidatePurposeExport("marketing-enrichment", true));
        Assert.NotNull(guard.ValidateCertifiedConsumerBreak(true));
        Assert.NotNull(guard.ValidateSchemaCompat(false));
        Assert.NotNull(guard.ValidateDeprecationWindow(30));
        Assert.NotNull(guard.ValidateThrottleExceed(true, false));
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "ext005-api-exchange-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-EXT-005-production-api-exchange-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_005_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_005_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
