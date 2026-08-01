using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.SovKeys;
using HRManagement.ServiceDefaults.SovZt;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Sov004ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "sov-zt", "zero-trust-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["SovZt:EnforceOnStartup"] = "true",
            ["SovZt:LivePamClaim"] = "false",
            ["SovZt:MatrixPath"] = Path.Combine(RepoRoot, "docs", "sov-zt", "zero-trust-matrix.json")
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
        services.AddSovZt(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["SovZt:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<ISovZtProvider>();
        Assert.Equal("SOV-004", provider.Matrix.Card);
        Assert.True(provider.Matrix.NoStandingPrivilege?.SharedRootForbidden == true);
        Assert.True(provider.Matrix.PamJit?.ApprovalRequired == true);
        Assert.True(provider.Matrix.AdminWorkstations?.OrdinaryDeviceForbidden == true);
        Assert.True(provider.Matrix.BreakGlass?.ImmediateAlertRequired == true);
        Assert.True(provider.Matrix.MockSandbox?.LivePamForbidden == true);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-pam-workers" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SOV-005" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "NAT-001" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SOV-003" && b.Status == "Satisfied");
    }

    [Fact]
    public void Startup_fails_when_standing_privilege_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov004-sp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovZtMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-zt", "zero-trust-matrix.json")))!;
            matrix.NoStandingPrivilege!.PermanentAdminForbidden = false;
            var matrixPath = Path.Combine(tmp, "zero-trust-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovZt:EnforceOnStartup"] = "true",
                ["SovZt:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovZtStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("permanentAdminForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void Startup_fails_when_jit_approval_not_required()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov004-jit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovZtMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-zt", "zero-trust-matrix.json")))!;
            matrix.PamJit!.ApprovalRequired = false;
            var matrixPath = Path.Combine(tmp, "zero-trust-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovZt:EnforceOnStartup"] = "true",
                ["SovZt:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovZtStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("approvalRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void Startup_fails_when_ordinary_device_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov004-od-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovZtMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-zt", "zero-trust-matrix.json")))!;
            matrix.AdminWorkstations!.OrdinaryDeviceForbidden = false;
            var matrixPath = Path.Combine(tmp, "zero-trust-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovZt:EnforceOnStartup"] = "true",
                ["SovZt:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovZtStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("ordinaryDeviceForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void Startup_fails_when_break_glass_alert_not_required()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov004-bg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovZtMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-zt", "zero-trust-matrix.json")))!;
            matrix.BreakGlass!.ImmediateAlertRequired = false;
            var matrixPath = Path.Combine(tmp, "zero-trust-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovZt:EnforceOnStartup"] = "true",
                ["SovZt:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovZtStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("immediateAlertRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public void LivePamClaim_fails_until_live_pam_workers()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["SovZt:EnforceOnStartup"] = "true",
            ["SovZt:LivePamClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<SovZtStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LivePamClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix_and_live_Blocked()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["SovZt:EnforceOnStartup"] = "true",
            ["SovZt:LivePamClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<SovZtStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var provider = sp.GetRequiredService<ISovZtProvider>();
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-pam-workers" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SOV-005" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "NAT-001" && b.Status == "NotStarted");

        var guard = sp.GetRequiredService<IPamClaimGuard>();
        Assert.Null(guard.EnsureLivePamAllowed());
        Assert.NotNull(guard.ValidateNoStandingPrivilege(true, false));
        Assert.NotNull(guard.ValidateJit(true, true, false));
        Assert.NotNull(guard.ValidateAdminWorkstation(true));
        Assert.NotNull(guard.ValidateBreakGlass(false, true));
        Assert.Null(guard.ValidateNoStandingPrivilege(false, false));
        Assert.Null(guard.ValidateJit(true, true, true));
    }

    [Fact]
    public void Sov003_accepts_SOV004_Satisfied_without_raising_LiveKeyCustody()
    {
        using var sov3 = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "sov-keys", "key-sovereignty-matrix.json")));
        var s3 = sov3.RootElement.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", s3["SOV-004"]);
        Assert.Equal("NotStarted", s3["SIG-001"]);
        Assert.Equal("NotStarted", s3["NAT-001"]);

        var failures = SovKeysEvaluator.EvaluateBlockers(
            JsonSerializer.Deserialize<SovKeysMatrixDocument>(
                System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "sov-keys", "key-sovereignty-matrix.json")))!);
        Assert.DoesNotContain(failures, f => f.Contains("SOV-004", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "sov004-zero-trust-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-SOV-004-production-zero-trust-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_004_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_004_INDEPENDENT_REVIEW_WAIVER.md")));
        var backlog = System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "completion-backlog", "08-NATIONAL-ASSURANCE-OPERATIONS.md"));
        Assert.Contains("Done (production zero-trust binding)", backlog, StringComparison.Ordinal);
    }
}
