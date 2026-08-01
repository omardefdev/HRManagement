using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.ProdBranch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Prod004ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "prod-branch", "remote-branch-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ProdBranch:EnforceOnStartup"] = "true",
            ["ProdBranch:LiveRemoteBranchClaim"] = "false",
            ["ProdBranch:MatrixPath"] = Path.Combine(RepoRoot, "docs", "prod-branch", "remote-branch-matrix.json")
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
        services.AddProdBranch(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["ProdBranch:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IProdBranchProvider>();
        Assert.Equal("PROD-004", provider.Matrix.Card);
        Assert.True(provider.Matrix.Accessibility?.Wcag22AaRequired == true);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "L10N-001" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "MOB-001" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-remote-branch-ux" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-pwa-fleet" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-offline-field" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "UX-002" && b.Status == "Satisfied");
        Assert.True(provider.Matrix.MockSandbox?.LiveRemoteBranchForbidden == true);
    }

    [Fact]
    public void Startup_fails_when_performance_budgets_not_required()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod004-bb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdBranchMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-branch", "remote-branch-matrix.json")))!;
            matrix.PerformanceBudgets!.Required = false;
            var matrixPath = Path.Combine(tmp, "remote-branch-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdBranch:EnforceOnStartup"] = "true",
                ["ProdBranch:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdBranchStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("performanceBudgets.required", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_live_remote_branch_not_forbidden()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod004-lrb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdBranchMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-branch", "remote-branch-matrix.json")))!;
            matrix.MockSandbox!.LiveRemoteBranchForbidden = false;
            var matrixPath = Path.Combine(tmp, "remote-branch-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdBranch:EnforceOnStartup"] = "true",
                ["ProdBranch:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdBranchStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("liveRemoteBranchForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_localization_arabic_english_missing()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod004-loc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdBranchMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-branch", "remote-branch-matrix.json")))!;
            matrix.Localization!.ArabicEnglishRequired = false;
            var matrixPath = Path.Combine(tmp, "remote-branch-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdBranch:EnforceOnStartup"] = "true",
                ["ProdBranch:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdBranchStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("arabicEnglishRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_offline_encryption_not_required()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod004-off-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdBranchMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-branch", "remote-branch-matrix.json")))!;
            matrix.Offline!.EncryptionRequired = false;
            var matrixPath = Path.Combine(tmp, "remote-branch-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdBranch:EnforceOnStartup"] = "true",
                ["ProdBranch:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdBranchStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("encryptionRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LiveRemoteBranchClaim_fails_until_workers()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["ProdBranch:EnforceOnStartup"] = "true",
            ["ProdBranch:LiveRemoteBranchClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ProdBranchStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveRemoteBranchClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix_and_honest_NotStarted()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["ProdBranch:EnforceOnStartup"] = "true",
            ["ProdBranch:LiveRemoteBranchClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ProdBranchStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var provider = sp.GetRequiredService<IProdBranchProvider>();
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "L10N-001" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "MOB-001" && b.Status == "NotStarted");
        var guard = sp.GetRequiredService<IRemoteBranchClaimGuard>();
        Assert.Null(guard.EnsureLiveRemoteBranchAllowed());
        Assert.NotNull(guard.ValidatePerformanceBudget(false));
        Assert.NotNull(guard.ValidateNoDataLossOnInterrupt(true, false));
        Assert.NotNull(guard.ValidateLocalizationReady(false, true));
        Assert.NotNull(guard.ValidateOfflinePolicy(false, true, true));
        Assert.NotNull(guard.ValidateWcagRequired(false));
        Assert.NotNull(guard.ValidateIdempotentRetry(false));
    }

    [Fact]
    public void Upstream_PROD003_matrix_marks_PROD004_Satisfied()
    {
        using var prod003 = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "prod-ess", "employee-selfservice-matrix.json")));
        var p3 = prod003.RootElement.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", p3["PROD-004"]);
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "prod004-remote-branch-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-PROD-004-production-remote-branch-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_004_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_004_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
