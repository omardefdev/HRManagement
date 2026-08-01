using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.ProdEss;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Prod003ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "prod-ess", "employee-selfservice-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ProdEss:EnforceOnStartup"] = "true",
            ["ProdEss:LiveEmployeeSelfServiceClaim"] = "false",
            ["ProdEss:MatrixPath"] = Path.Combine(RepoRoot, "docs", "prod-ess", "employee-selfservice-matrix.json")
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
        services.AddProdEss(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_honest_blockers()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["ProdEss:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IProdEssProvider>();
        Assert.Equal("PROD-003", provider.Matrix.Card);
        Assert.True(provider.Matrix.ManagerScope?.OutsideScopeForbidden == true);
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "IAM-002" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-employee-portal" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-manager-portal" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "live-usage-telemetry" && b.Status == "Blocked");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "ESS-001" && b.Status == "NotStarted");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "IAM-003" && b.Status == "Satisfied");
        Assert.True(provider.Matrix.MockSandbox?.LiveEmployeePortalForbidden == true);
    }

    [Fact]
    public void Startup_fails_when_manager_outside_scope_allowed()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod003-mos-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdEssMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-ess", "employee-selfservice-matrix.json")))!;
            matrix.ManagerScope!.OutsideScopeForbidden = false;
            var matrixPath = Path.Combine(tmp, "employee-selfservice-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdEss:EnforceOnStartup"] = "true",
                ["ProdEss:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdEssStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("outsideScopeForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_live_employee_portal_not_forbidden()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod003-lep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdEssMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-ess", "employee-selfservice-matrix.json")))!;
            matrix.MockSandbox!.LiveEmployeePortalForbidden = false;
            var matrixPath = Path.Combine(tmp, "employee-selfservice-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdEss:EnforceOnStartup"] = "true",
                ["ProdEss:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdEssStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("liveEmployeePortalForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_correction_source_hidden()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod003-csh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdEssMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-ess", "employee-selfservice-matrix.json")))!;
            matrix.CorrectionVisibility!.SourceVisible = false;
            var matrixPath = Path.Combine(tmp, "employee-selfservice-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdEss:EnforceOnStartup"] = "true",
                ["ProdEss:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdEssStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("sourceVisible", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Startup_fails_when_usage_live_not_forbidden()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "prod003-ul-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var matrix = JsonSerializer.Deserialize<ProdEssMatrixDocument>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "prod-ess", "employee-selfservice-matrix.json")))!;
            matrix.UsageTelemetry!.LiveForbidden = false;
            var matrixPath = Path.Combine(tmp, "employee-selfservice-matrix.json");
            System.IO.File.WriteAllText(matrixPath, JsonSerializer.Serialize(matrix));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["ProdEss:EnforceOnStartup"] = "true",
                ["ProdEss:MatrixPath"] = matrixPath
            });
            var validator = sp.GetServices<IHostedService>().OfType<ProdEssStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("liveForbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void LiveEmployeeSelfServiceClaim_fails_until_workers()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["ProdEss:EnforceOnStartup"] = "true",
            ["ProdEss:LiveEmployeeSelfServiceClaim"] = "true"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ProdEssStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("LiveEmployeeSelfServiceClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_passes_with_current_matrix_and_IAM002_NotStarted()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["ProdEss:EnforceOnStartup"] = "true",
            ["ProdEss:LiveEmployeeSelfServiceClaim"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<ProdEssStartupValidator>().Single();
        validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var provider = sp.GetRequiredService<IProdEssProvider>();
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "IAM-002" && b.Status == "NotStarted");
        var guard = sp.GetRequiredService<IEmployeeSelfServiceClaimGuard>();
        Assert.Null(guard.EnsureLiveEssAllowed());
        Assert.NotNull(guard.ValidateManagerScope(false));
        Assert.NotNull(guard.ValidateCorrectionVisibility(false, true));
        Assert.NotNull(guard.ValidateUsageMeasured(false));
        Assert.NotNull(guard.ValidateHighVolumeJourney(false, false));
        Assert.NotNull(guard.ValidateLostSessionHandled(true, false));
    }

    [Fact]
    public void Upstream_matrices_mark_PROD003_Satisfied()
    {
        using var prod002 = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "prod-agency", "agency-selfadmin-matrix.json")));
        var p2 = prod002.RootElement.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", p2["PROD-003"]);

        using var str007 = JsonDocument.Parse(System.IO.File.ReadAllText(
            RepoPaths.Resolve("docs", "str-value", "kpi-matrix.json")));
        var s7 = str007.RootElement.GetProperty("blockers").EnumerateArray()
            .ToDictionary(b => b.GetProperty("id").GetString()!, b => b.GetProperty("status").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Satisfied", s7["PROD-003"]);
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "prod003-employee-selfservice-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-PROD-003-production-employee-selfservice-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_003_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "PROD_003_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
