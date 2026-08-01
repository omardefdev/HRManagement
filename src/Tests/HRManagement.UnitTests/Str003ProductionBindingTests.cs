using System.Diagnostics;
using System.Text.Json;
using HRManagement.Gateway.Services;
using HRManagement.ServiceDefaults.ModuleCatalog;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Str003ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var catalog = RepoPaths.Resolve("docs", "str-modules", "module-catalog.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(catalog)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ModuleCatalog:EnforceOnStartup"] = "true",
            ["ModuleCatalog:EnforcePathEntitlement"] = "true",
            ["ModuleCatalog:AllowNotReadyExposure"] = "false",
            ["ModuleCatalog:ValidateEvidenceFilesOnDisk"] = "false",
            ["ModuleCatalog:CatalogPath"] = Path.Combine(RepoRoot, "docs", "str-modules", "module-catalog.json")
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
        services.AddModuleCatalog(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_catalog_and_agency_visible_ready()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["ModuleCatalog:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<IModuleCatalogProvider>();
        Assert.Equal("STR-003", provider.Catalog.Card);
        Assert.Equal(14, provider.PilotApprovedSlugs.Count);
        var visible = provider.GetAgencyVisibleModules();
        Assert.All(visible, m => Assert.Equal("Ready", m.ReadinessStatus));
        Assert.DoesNotContain(visible, m => m.ModuleId is "postgres" or "rabbitmq");
    }

    [Fact]
    public void Evaluator_fails_EnabledModules_with_NotReady()
    {
        var catalog = JsonSerializer.Deserialize<ModuleCatalogDocument>(
            System.IO.File.ReadAllText(Path.Combine(RepoRoot, "docs", "str-modules", "module-catalog.json")))!;
        var target = catalog.Modules.First(m => m.ModuleId == "payroll");
        target.ReadinessStatus = "NotReady";
        var failures = ModuleCatalogEvaluator.Evaluate(
            catalog,
            ["payroll"],
            allowNotReadyExposure: false,
            validateEvidenceFilesOnDisk: false,
            repoRoot: null);
        Assert.Contains(failures, f => f.Contains("payroll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Startup_rejects_NotReady_in_EnabledModules()
    {
        using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
        {
            ["ModuleCatalog:EnforceOnStartup"] = "true",
            ["Gateway:EnabledModules:0"] = "payroll"
        });
        // Mutate loaded catalog in place via evaluator path: rebuild with bad catalog file is heavy;
        // use EnsureEnabledModulesValid after forcing NotReady through a custom evaluate.
        var provider = sp.GetRequiredService<IModuleCatalogProvider>();
        var payroll = provider.GetModule("payroll")!;
        payroll.ReadinessStatus = "NotReady";
        var validator = sp.GetServices<IHostedService>().OfType<ModuleCatalogStartupValidator>().Single();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("STR-003", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Guard_rejects_NotReady_and_filters_gateway_tiles()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["ModuleCatalog:EnforceOnStartup"] = "false",
            ["Gateway:EnabledModules:0"] = "hr-core",
            ["Gateway:EnabledModules:1"] = "payroll"
        });
        var guard = sp.GetRequiredService<IModuleEntitlementGuard>();
        Assert.True(guard.IsModuleExposed("hr-core"));
        Assert.True(guard.IsModuleExposed("payroll"));
        Assert.False(guard.IsModuleExposed("hr-health"));

        var cfg = sp.GetRequiredService<IConfiguration>();
        var tiles = new GatewayModuleCatalog(cfg, guard);
        Assert.Equal(2, tiles.Modules.Count);
        Assert.Null(tiles.FindBySlug("hr-health"));
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "str003-module-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-STR-003-production-module-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_003_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_003_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
