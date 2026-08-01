using System.Diagnostics;
using System.Text.Json;
using HRManagement.ServiceDefaults.LegalControl;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Str002ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var catalog = RepoPaths.Resolve("docs", "str-legal", "control-catalog.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(catalog)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["LegalControl:ReleaseProfile"] = "Interim",
            ["LegalControl:EnforceOnStartup"] = "true",
            ["LegalControl:AllowBlockedLegalNationalClaim"] = "false",
            ["LegalControl:EnforceLegislativeWatch"] = "true",
            ["LegalControl:CatalogPath"] = Path.Combine(RepoRoot, "docs", "str-legal", "control-catalog.json"),
            ["LegalControl:LegislativeWatchStampPath"] =
                Path.Combine(RepoRoot, "docs", "str-legal", "legislative-watch-stamp.json")
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
        services.AddLegalControl(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_catalog_and_interim_profile()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["LegalControl:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<ILegalControlCatalogProvider>();
        Assert.Equal("Interim", provider.ReleaseProfile);
        Assert.Equal("STR-002", provider.Catalog.Card);
        Assert.True(provider.Catalog.Controls.Count >= 10);
        Assert.NotNull(provider.GetControl("CTRL-PAY-NO-LEGAL-TBD"));
        Assert.True(provider.IsExceptionActive("EX-STR2-TAX-INTERIM"));
    }

    [Fact]
    public void Evaluator_fails_Implemented_with_unresolved_legal_marker()
    {
        var marker = LegalControlCatalogEvaluator.UnresolvedLegalMarker;
        var catalog = JsonSerializer.Deserialize<ControlCatalogDocument>(
            System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "str-legal", "control-catalog.json")))!;
        catalog.Controls.Add(new CatalogControl
        {
            RequirementId = "CTRL-TEST-TBD",
            Domain = "payroll",
            Title = "bad",
            SourceRef = "x",
            Confidence = "A",
            Status = "Implemented",
            EvidencePath = marker,
            TestRef = "x"
        });
        var failures = LegalControlCatalogEvaluator.Evaluate(catalog, "Interim", false);
        Assert.Contains(failures, f => f.Contains("CTRL-TEST-TBD", StringComparison.Ordinal)
                                       && f.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluator_fails_Partial_without_exception()
    {
        var catalog = new ControlCatalogDocument
        {
            Card = "STR-002",
            Controls =
            [
                new CatalogControl
                {
                    RequirementId = "CTRL-PARTIAL-BARE",
                    Domain = "labor",
                    Title = "bare",
                    Confidence = "B",
                    Status = "Partial",
                    ExceptionId = null,
                    EvidencePath = "x",
                    TestRef = "y"
                }
            ]
        };
        var failures = LegalControlCatalogEvaluator.Evaluate(catalog, "Interim", false);
        Assert.Contains(failures, f => f.Contains("CTRL-PARTIAL-BARE", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_startup_rejects_NationalClaim_with_Blocked_Legal()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["LegalControl:ReleaseProfile"] = "NationalClaim",
            ["LegalControl:AllowBlockedLegalNationalClaim"] = "false",
            ["LegalControl:EnforceOnStartup"] = "true",
            ["LegalControl:EnforceLegislativeWatch"] = "false"
        });
        var validator = sp.GetServices<IHostedService>().OfType<LegalControlStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("NationalClaim", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Guard_rejects_Blocked_Legal_capability()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["LegalControl:EnforceOnStartup"] = "false"
        });
        var guard = sp.GetRequiredService<ILegalControlGuard>();
        Assert.False(guard.IsCapabilityAllowed("CTRL-TAX-SYD004-MOF"));
        Assert.False(guard.IsCapabilityAllowed("CTRL-PRV-SYD008-LICENSE"));
        Assert.True(guard.IsCapabilityAllowed("CTRL-PAY-NO-LEGAL-TBD"));
        Assert.Null(guard.ValidatePayrollRelease());
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "str002-control-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-STR-002-production-control-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_002_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_002_INDEPENDENT_REVIEW_WAIVER.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "str-legal", "legislative-watch-stamp.json")));
    }
}
