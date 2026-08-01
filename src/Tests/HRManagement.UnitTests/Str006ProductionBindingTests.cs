using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.Ownership;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Str006ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "str-ownership", "ownership-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ProgramOwnership:EnforceOnStartup"] = "true",
            ["ProgramOwnership:National24x7Claim"] = "false",
            ["ProgramOwnership:MatrixPath"] = Path.Combine(RepoRoot, "docs", "str-ownership", "ownership-matrix.json")
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
        services.AddOwnership(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["ProgramOwnership:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IOwnershipProvider>();
        Assert.Equal("STR-006", provider.Matrix.Card);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "STR-005" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SOV-007" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "FIN-001" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "SRE-001" && b.Status == "Blocked");
        Assert.False(provider.Matrix.Staffing?.National24x7Claim ?? true);
        Assert.NotEmpty(provider.Matrix.Components);
    }

    [Fact]
    public void Startup_fails_on_critical_low_bus_factor()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "str006-bf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<OwnershipMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "str-ownership", "ownership-matrix.json")))!;
            var critical = matrix.Components.First(c => c.Critical);
            critical.BusFactor = 1;
            critical.BackupRoles = [];
            var matrixPath = Path.Combine(tmp, "ownership-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProgramOwnership:EnforceOnStartup"] = "true",
                ["ProgramOwnership:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<OwnershipStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("STR-006", ex.Message, StringComparison.Ordinal);
            Assert.Contains("busFactor", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_on_vendor_missing_exit()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "str006-v-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<OwnershipMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "str-ownership", "ownership-matrix.json")))!;
            matrix.Vendors[0].Clauses = ["RTO", "IncidentDisclosure"];
            matrix.Vendors[0].ExitAssistance = false;
            var matrixPath = Path.Combine(tmp, "ownership-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProgramOwnership:EnforceOnStartup"] = "true",
                ["ProgramOwnership:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<OwnershipStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("Exit", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void National24x7Claim_fails_while_sre001_blocked()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["ProgramOwnership:EnforceOnStartup"] = "true",
            ["ProgramOwnership:National24x7Claim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<OwnershipStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("National24x7", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["ProgramOwnership:EnforceOnStartup"] = "true",
            ["ProgramOwnership:National24x7Claim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<OwnershipStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var guard = sp.GetRequiredService<ISustainmentClaimGuard>();
        Assert.Null(guard.EnsureNational24x7Allowed());
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "str006-ownership-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-STR-006-production-ownership-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_006_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_006_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
