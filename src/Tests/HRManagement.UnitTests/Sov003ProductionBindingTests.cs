using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.ExtPki;
using HRManagement.ServiceDefaults.SovData;
using HRManagement.ServiceDefaults.SovKeys;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Sov003ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "sov-keys", "key-sovereignty-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["SovKeys:EnforceOnStartup"] = "true",
            ["SovKeys:LiveKeyCustodyClaim"] = "false",
            ["SovKeys:MatrixPath"] = Path.Combine(RepoRoot, "docs", "sov-keys", "key-sovereignty-matrix.json")
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
        services.AddSovKeys(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["SovKeys:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<ISovKeysProvider>();
        Assert.Equal("SOV-003", provider.Matrix.Card);
        Assert.True(provider.Matrix.HsmKms?.VendorAloneCannotDecrypt == true);
        Assert.True(provider.Matrix.DualControl?.Required == true);
        Assert.True(provider.Matrix.TrustedTime?.ClockDriftBoundRequired == true);
        Assert.True(provider.Matrix.MockSandbox?.LiveHsmForbidden == true);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SEC-004" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-hsm-prod" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "EXT-004" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SOV-004" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SIG-001" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "NAT-001" && b.Status == "NotStarted");
    }

    [Fact]
    public void Startup_fails_when_vendor_alone_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov003-va-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovKeysMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-keys", "key-sovereignty-matrix.json")))!;
            matrix.HsmKms!.VendorAloneCannotDecrypt = false;
            var matrixPath = Path.Combine(tmp, "key-sovereignty-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovKeys:EnforceOnStartup"] = "true",
                ["SovKeys:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovKeysStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("vendorAloneCannotDecrypt", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_dual_control_missing()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov003-dc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovKeysMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-keys", "key-sovereignty-matrix.json")))!;
            matrix.DualControl!.Required = false;
            var matrixPath = Path.Combine(tmp, "key-sovereignty-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovKeys:EnforceOnStartup"] = "true",
                ["SovKeys:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovKeysStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("dualControl", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_rotation_drill_not_required()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov003-rot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovKeysMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-keys", "key-sovereignty-matrix.json")))!;
            matrix.Rotation!.CustodianLossDrillRequired = false;
            var matrixPath = Path.Combine(tmp, "key-sovereignty-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovKeys:EnforceOnStartup"] = "true",
                ["SovKeys:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovKeysStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("custodianLossDrillRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_clock_drift_unbounded()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "sov003-clk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<SovKeysMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "sov-keys", "key-sovereignty-matrix.json")))!;
            matrix.TrustedTime!.ClockDriftBoundRequired = false;
            var matrixPath = Path.Combine(tmp, "key-sovereignty-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["SovKeys:EnforceOnStartup"] = "true",
                ["SovKeys:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<SovKeysStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("clockDriftBoundRequired", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LiveKeyCustodyClaim_fails_until_hsm_prod()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["SovKeys:EnforceOnStartup"] = "true",
            ["SovKeys:LiveKeyCustodyClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<SovKeysStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveKeyCustodyClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix_and_SEC004_Blocked()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["SovKeys:EnforceOnStartup"] = "true",
            ["SovKeys:LiveKeyCustodyClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<SovKeysStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var provider = sp.GetRequiredService<ISovKeysProvider>();
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SEC-004" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SOV-004" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SIG-001" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "NAT-001" && b.Status == "NotStarted");

        var guard = sp.GetRequiredService<IKeyCustodyClaimGuard>();
        Assert.Null(guard.EnsureLiveKeyCustodyAllowed());
        Assert.NotNull(guard.ValidateVendorAloneCannotDecrypt(true));
        Assert.NotNull(guard.ValidateDualControl(false));
        Assert.NotNull(guard.ValidateRotation(false));
        Assert.NotNull(guard.ValidateTrustedTime(false));
        Assert.Null(guard.ValidateVendorAloneCannotDecrypt(false));
        Assert.Null(guard.ValidateDualControl(true));
    }

    [Fact]
    public void Sov002_and_Ext004_accept_SOV003_Satisfied_and_hsm_prod_Blocked()
    {
        using var sov2 = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "sov-data", "data-sovereignty-matrix.json")));
        var s2 = sov2.RootElement.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", s2["SOV-003"]);

        using var ext4 = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "ext-pki", "pki-signature-matrix.json")));
        var e4 = ext4.RootElement.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", e4["SOV-003"]);
        Assert.Equal("Blocked", e4["hsm-prod"]);

        var sovFailures = SovDataEvaluator.EvaluateBlockers(
            JsonSerializer.Deserialize<SovDataMatrixDocument>(
                System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "sov-data", "data-sovereignty-matrix.json")))!);
        Assert.DoesNotContain(sovFailures, f => f.Contains("SOV-003", StringComparison.OrdinalIgnoreCase));

        var extFailures = ExtPkiEvaluator.EvaluateBlockers(
            JsonSerializer.Deserialize<ExtPkiMatrixDocument>(
                System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "ext-pki", "pki-signature-matrix.json")))!);
        Assert.DoesNotContain(extFailures, f => f.Contains("SOV-003", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "sov003-key-sovereignty-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-SOV-003-production-key-sovereignty-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_003_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "SOV_003_INDEPENDENT_REVIEW_WAIVER.md")));
        var backlog = System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "completion-backlog", "08-NATIONAL-ASSURANCE-OPERATIONS.md"));
        Assert.Contains("Done (production key-sovereignty binding)", backlog, StringComparison.Ordinal);
    }
}
