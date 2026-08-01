using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.ExtTreasury;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Ext003ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "ext-treasury", "treasury-payment-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ExtTreasury:EnforceOnStartup"] = "true",
            ["ExtTreasury:LiveTreasuryClaim"] = "false",
            ["ExtTreasury:MatrixPath"] = Path.Combine(RepoRoot, "docs", "ext-treasury", "treasury-payment-matrix.json")
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
        services.AddExtTreasury(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["ExtTreasury:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IExtTreasuryProvider>();
        Assert.Equal("EXT-003", provider.Matrix.Card);
        Assert.True(provider.Matrix.ApprovedRunRequired);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "treasury-live" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "bank-sandbox-cert" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "postal-channel-live" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "EXT-004" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "PAY-003" && b.Status == "Satisfied");
        Assert.True(provider.Matrix.MockSandbox?.LiveTreasuryForbidden == true);
    }

    [Fact]
    public void Startup_fails_when_approved_run_not_required()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext003-approved-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtTreasuryMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-treasury", "treasury-payment-matrix.json")))!;
            matrix.ApprovedRunRequired = false;
            var matrixPath = Path.Combine(tmp, "treasury-payment-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtTreasury:EnforceOnStartup"] = "true",
                ["ExtTreasury:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtTreasuryStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("approvedRunRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_live_treasury_not_forbidden()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext003-treasury-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtTreasuryMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-treasury", "treasury-payment-matrix.json")))!;
            matrix.MockSandbox!.LiveTreasuryForbidden = false;
            var matrixPath = Path.Combine(tmp, "treasury-payment-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtTreasury:EnforceOnStartup"] = "true",
                ["ExtTreasury:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtTreasuryStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("liveTreasuryForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_duplicate_policy_not_reject()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext003-dup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtTreasuryMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-treasury", "treasury-payment-matrix.json")))!;
            matrix.ExactlyOnce!.DuplicatePolicy = "allow";
            var matrixPath = Path.Combine(tmp, "treasury-payment-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtTreasury:EnforceOnStartup"] = "true",
                ["ExtTreasury:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtTreasuryStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("duplicatePolicy", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_unaudited_manual_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext003-manual-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtTreasuryMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-treasury", "treasury-payment-matrix.json")))!;
            matrix.ManualFiles!.UnauditedForbidden = false;
            var matrixPath = Path.Combine(tmp, "treasury-payment-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtTreasury:EnforceOnStartup"] = "true",
                ["ExtTreasury:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtTreasuryStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("unauditedForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LiveTreasuryClaim_fails_until_workers()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["ExtTreasury:EnforceOnStartup"] = "true",
            ["ExtTreasury:LiveTreasuryClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ExtTreasuryStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveTreasuryClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["ExtTreasury:EnforceOnStartup"] = "true",
            ["ExtTreasury:LiveTreasuryClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ExtTreasuryStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var guard = sp.GetRequiredService<ITreasuryPaymentClaimGuard>();
        Assert.Null(guard.EnsureLiveTreasuryAllowed());
        Assert.NotNull(guard.ValidateApprovedRun("Draft"));
        Assert.NotNull(guard.ValidateIdempotency(true, true));
        Assert.NotNull(guard.ValidateTotals(100m, 99m, true));
        Assert.NotNull(guard.ValidateManualFile(false));
        Assert.NotNull(guard.ValidateCutoff(withinCutoff: false, contingencyHold: false));
        Assert.NotNull(guard.ValidateLivePkiClaim(true));
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "ext003-treasury-payment-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-EXT-003-production-treasury-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_003_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_003_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
