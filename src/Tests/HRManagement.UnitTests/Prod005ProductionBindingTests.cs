using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.ProdWorkbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Prod005ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "prod-workbox", "workbox-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ProdWorkbox:EnforceOnStartup"] = "true",
            ["ProdWorkbox:LiveWorkboxClaim"] = "false",
            ["ProdWorkbox:MatrixPath"] = Path.Combine(RepoRoot, "docs", "prod-workbox", "workbox-matrix.json")
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
        services.AddProdWorkbox(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["ProdWorkbox:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IProdWorkboxProvider>();
        Assert.Equal("PROD-005", provider.Matrix.Card);
        Assert.True(provider.Matrix.SafeSearch?.ExistenceLeakForbidden == true);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "IAM-002" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "COM-001" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-workbox-ui" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-notification-delivery" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "INT-003" && b.Status == "Satisfied");
        Assert.True(provider.Matrix.MockSandbox?.LiveWorkboxForbidden == true);
    }

    [Fact]
    public void Startup_fails_when_no_lost_cross_module_false()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod005-lost-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdWorkboxMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-workbox", "workbox-matrix.json")))!;
            matrix.QueueCompleteness!.NoLostCrossModule = false;
            var matrixPath = Path.Combine(tmp, "workbox-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdWorkbox:EnforceOnStartup"] = "true",
                ["ProdWorkbox:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdWorkboxStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("noLostCrossModule", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_existence_leak_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod005-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdWorkboxMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-workbox", "workbox-matrix.json")))!;
            matrix.SafeSearch!.ExistenceLeakForbidden = false;
            var matrixPath = Path.Combine(tmp, "workbox-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdWorkbox:EnforceOnStartup"] = "true",
                ["ProdWorkbox:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdWorkboxStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("existenceLeakForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_bulk_limits_not_required()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod005-bulk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdWorkboxMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-workbox", "workbox-matrix.json")))!;
            matrix.BulkActions!.LimitsRequired = false;
            var matrixPath = Path.Combine(tmp, "workbox-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdWorkbox:EnforceOnStartup"] = "true",
                ["ProdWorkbox:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdWorkboxStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("limitsRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_secrets_in_notification_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod005-sec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdWorkboxMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-workbox", "workbox-matrix.json")))!;
            matrix.Notifications!.NoSecretsInPayload = false;
            var matrixPath = Path.Combine(tmp, "workbox-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdWorkbox:EnforceOnStartup"] = "true",
                ["ProdWorkbox:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdWorkboxStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("noSecretsInPayload", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_live_workbox_not_forbidden()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod005-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdWorkboxMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-workbox", "workbox-matrix.json")))!;
            matrix.MockSandbox!.LiveWorkboxForbidden = false;
            var matrixPath = Path.Combine(tmp, "workbox-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdWorkbox:EnforceOnStartup"] = "true",
                ["ProdWorkbox:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdWorkboxStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("liveWorkboxForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LiveWorkboxClaim_fails_until_workers()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["ProdWorkbox:EnforceOnStartup"] = "true",
            ["ProdWorkbox:LiveWorkboxClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ProdWorkboxStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveWorkboxClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix_and_IAM002_NotStarted()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["ProdWorkbox:EnforceOnStartup"] = "true",
            ["ProdWorkbox:LiveWorkboxClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ProdWorkboxStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var provider = sp.GetRequiredService<IProdWorkboxProvider>();
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "IAM-002" && b.Status == "NotStarted");
        var guard = sp.GetRequiredService<IWorkboxClaimGuard>();
        Assert.Null(guard.EnsureLiveWorkboxAllowed());
        Assert.NotNull(guard.ValidateNoLostTransaction(false));
        Assert.NotNull(guard.ValidateSafeSearch(false, true, true));
        Assert.NotNull(guard.ValidateBulkApprove(false, true));
        Assert.NotNull(guard.ValidateNotificationSafe(true, true, true));
        Assert.NotNull(guard.ValidateDeepLink(true, false));
    }

    [Fact]
    public void Upstream_matrices_mark_PROD005_Satisfied()
    {
        using var prod004 = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "prod-branch", "remote-branch-matrix.json")));
        var p4 = prod004.RootElement.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", p4["PROD-005"]);

        using var ext007 = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "ext-sector", "sector-catalog-matrix.json")));
        var e7 = ext007.RootElement.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", e7["PROD-005"]);
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "prod005-workbox-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-PROD-005-production-workbox-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_005_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_005_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
