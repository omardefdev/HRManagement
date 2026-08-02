using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.ExtSandbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Ext006ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "ext-sandbox", "sandbox-cert-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ExtSandbox:EnforceOnStartup"] = "true",
            ["ExtSandbox:LiveSandboxPortalClaim"] = "false",
            ["ExtSandbox:MatrixPath"] = Path.Combine(RepoRoot, "docs", "ext-sandbox", "sandbox-cert-matrix.json")
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
        services.AddExtSandbox(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["ExtSandbox:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IExtSandboxProvider>();
        Assert.Equal("EXT-006", provider.Matrix.Card);
        Assert.True(provider.Matrix.SyntheticDataOnly);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-developer-portal" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "real-pii-in-sandbox" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "partner-cert-fleet-live" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "EXT-007" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "EXT-005" && b.Status == "Satisfied");
        Assert.True(provider.Matrix.MockSandbox?.LivePortalForbidden == true);
    }

    [Fact]
    public void Startup_fails_when_synthetic_not_required()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext006-synth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtSandboxMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-sandbox", "sandbox-cert-matrix.json")))!;
            matrix.SyntheticDataOnly = false;
            var matrixPath = Path.Combine(tmp, "sandbox-cert-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtSandbox:EnforceOnStartup"] = "true",
                ["ExtSandbox:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtSandboxStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("syntheticDataOnly", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_live_portal_not_forbidden()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext006-portal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtSandboxMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-sandbox", "sandbox-cert-matrix.json")))!;
            matrix.MockSandbox!.LivePortalForbidden = false;
            var matrixPath = Path.Combine(tmp, "sandbox-cert-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtSandbox:EnforceOnStartup"] = "true",
                ["ExtSandbox:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtSandboxStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("livePortalForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_incompatible_client_may_pass()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ext006-incompat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ExtSandboxMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "ext-sandbox", "sandbox-cert-matrix.json")))!;
            matrix.Conformance!.IncompatibleClientMustFail = false;
            var matrixPath = Path.Combine(tmp, "sandbox-cert-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ExtSandbox:EnforceOnStartup"] = "true",
                ["ExtSandbox:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ExtSandboxStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("incompatibleClientMustFail", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LiveSandboxPortalClaim_fails_until_workers()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["ExtSandbox:EnforceOnStartup"] = "true",
            ["ExtSandbox:LiveSandboxPortalClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ExtSandboxStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveSandboxPortalClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["ExtSandbox:EnforceOnStartup"] = "true",
            ["ExtSandbox:LiveSandboxPortalClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ExtSandboxStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var guard = sp.GetRequiredService<ISandboxCertClaimGuard>();
        Assert.Null(guard.EnsureLivePortalAllowed());
        Assert.NotNull(guard.ValidateNoProdAccess(true));
        Assert.NotNull(guard.ValidateNoRealPii(true));
        Assert.NotNull(guard.ValidateEphemeralCredential(false, true));
        Assert.NotNull(guard.ValidateIncompatibleClient(true, false));
        Assert.NotNull(guard.ValidateDocsMatch(false));
        Assert.NotNull(guard.ValidateRevocation(true, false));
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "ext006-sandbox-cert-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-EXT-006-production-sandbox-cert-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_006_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "EXT_006_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
