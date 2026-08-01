using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.ValueKpi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Str007ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "str-value", "kpi-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ProgramValue:EnforceOnStartup"] = "true",
            ["ProgramValue:LiveDashboardClaim"] = "false",
            ["ProgramValue:MatrixPath"] = Path.Combine(RepoRoot, "docs", "str-value", "kpi-matrix.json")
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
        services.AddValueKpi(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["ProgramValue:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IValueKpiProvider>();
        Assert.Equal("STR-007", provider.Matrix.Card);
        Assert.True(provider.Matrix.Kpis.Count >= 9);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "FIN-001" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "PROD-003" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "STR-001" && b.Status == "Satisfied");
        Assert.True(provider.Matrix.PrivacyRules?.TenantScoped ?? false);
    }

    [Fact]
    public void Startup_fails_when_owner_equals_auditor()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "str007-oa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ValueKpiMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "str-value", "kpi-matrix.json")))!;
            matrix.Kpis[0].AuditorRole = matrix.Kpis[0].OwnerRole;
            var matrixPath = Path.Combine(tmp, "kpi-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProgramValue:EnforceOnStartup"] = "true",
                ["ProgramValue:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ValueKpiStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("STR-007", ex.Message, StringComparison.Ordinal);
            Assert.Contains("auditor", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_baseline_not_frozen()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "str007-bf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ValueKpiMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "str-value", "kpi-matrix.json")))!;
            matrix.Baselines[0].PrePilotFreeze = false;
            var matrixPath = Path.Combine(tmp, "kpi-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProgramValue:EnforceOnStartup"] = "true",
                ["ProgramValue:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ValueKpiStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("prePilotFreeze", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LiveDashboardClaim_fails_until_prod006c()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["ProgramValue:EnforceOnStartup"] = "true",
            ["ProgramValue:LiveDashboardClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ValueKpiStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveDashboardClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PROD-006c", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("live-bi-dashboard", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["ProgramValue:EnforceOnStartup"] = "true",
            ["ProgramValue:LiveDashboardClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ValueKpiStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var guard = sp.GetRequiredService<IValueBenefitGuard>();
        Assert.Null(guard.EnsureLiveDashboardAllowed());
        Assert.NotNull(guard.ValidateBenefitMeasure("screenCount"));
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "str007-value-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-STR-007-production-value-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_007_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_007_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
