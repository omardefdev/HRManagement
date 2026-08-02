using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.NationalScope;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Str001ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "str-scope", "envelope-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    [Fact]
    public void Provider_loads_syria_interim_and_ceilings()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NationalScope:ActiveEnvelope"] = "syria-interim",
                ["NationalScope:CurrencyCode"] = "SYP",
                ["NationalScope:MatrixPath"] = Path.Combine(RepoRoot, "docs", "str-scope", "envelope-matrix.json"),
                ["NationalScope:HardEnforceCeilings"] = "false"
            })
            .Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment
        {
            EnvironmentName = Environments.Development,
            ContentRootPath = RepoRoot,
            ApplicationName = "tests",
            ContentRootFileProvider = new PhysicalFileProvider(RepoRoot)
        });
        services.AddNationalScope(config);
        using var sp = services.BuildServiceProvider();
        var scope = sp.GetRequiredService<INationalScopeProvider>();
        Assert.Equal("syria-interim", scope.ActiveEnvelope);
        Assert.Equal(1_250_000m, scope.PayrollWindowPersonsCeiling);
        Assert.Equal(5_000_000m, scope.PersonsCeiling);
        Assert.Equal(1000m, scope.TenantCeiling);
        Assert.Equal("SYP", scope.ResolvedCurrencyCode);
    }

    [Fact]
    public void Production_startup_rejects_national_without_claim_flag()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NationalScope:ActiveEnvelope"] = "national-competition",
                ["NationalScope:AllowNationalCompetitionClaim"] = "false",
                ["NationalScope:MatrixPath"] = Path.Combine(RepoRoot, "docs", "str-scope", "envelope-matrix.json")
            })
            .Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment
        {
            EnvironmentName = Environments.Production,
            ContentRootPath = RepoRoot,
            ApplicationName = "tests",
            ContentRootFileProvider = new PhysicalFileProvider(RepoRoot)
        });
        services.AddNationalScope(config);
        using var sp = services.BuildServiceProvider();
        var validator = sp.GetServices<IHostedService>().OfType<NationalScopeStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() => validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("national-competition", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Hard_enforce_payroll_ceiling_fails_over_limit()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NationalScope:ActiveEnvelope"] = "syria-interim",
                ["NationalScope:HardEnforceCeilings"] = "true",
                ["NationalScope:MatrixPath"] = Path.Combine(RepoRoot, "docs", "str-scope", "envelope-matrix.json")
            })
            .Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment
        {
            EnvironmentName = Environments.Staging,
            ContentRootPath = RepoRoot,
            ApplicationName = "tests",
            ContentRootFileProvider = new PhysicalFileProvider(RepoRoot)
        });
        services.AddNationalScope(config);
        using var sp = services.BuildServiceProvider();
        var guard = sp.GetRequiredService<INationalScopeGuard>();
        var err = guard.CheckPayrollPersonCount(2_000_000);
        Assert.NotNull(err);
        Assert.Contains("payroll window", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Matrix_persons_match_nfr_floor_text()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "str-scope", "envelope-matrix.json")));
        var persons = doc.RootElement.GetProperty("indicators").EnumerateArray()
            .First(i => i.GetProperty("id").GetString() == "national.active_history_persons")
            .GetProperty("value").GetInt64();
        Assert.Equal(5_000_000L, persons);
        var nfr = System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "NFR_CAPACITY_MODEL.md"));
        Assert.True(nfr.Contains("5,000,000", StringComparison.Ordinal) || nfr.Contains("5000000", StringComparison.Ordinal));
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "str001-envelope-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-STR-001-production-scope-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_001_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_001_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
