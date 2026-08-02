using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using HRManagement.ServiceDefaults.ModuleCatalog;
using HRManagement.ServiceDefaults.NoFork;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Str004ProductionBindingTests
{

    private static string RepoRoot
    {
        get
        {
            var matrix = RepoPaths.Resolve("docs", "str-product", "nofork-matrix.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(matrix)!, "..", ".."));
        }
    }

    private static ServiceProvider BuildProvider(
        string environmentName,
        Dictionary<string, string?> overrides,
        Guid? tenantId = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["NoFork:EnforceOnStartup"] = "true",
            ["NoFork:ResolveAgencyPacks"] = "true",
            ["NoFork:MatrixPath"] = Path.Combine(RepoRoot, "docs", "str-product", "nofork-matrix.json"),
            ["NoFork:AgencyPacksDirectory"] = Path.Combine(RepoRoot, "testdata", "str004", "agencies"),
            ["ModuleCatalog:EnforceOnStartup"] = "false",
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

        var http = new DefaultHttpContext();
        if (tenantId.HasValue)
        {
            var identity = new ClaimsIdentity(
            [
                new Claim("tenant_id", tenantId.Value.ToString())
            ], "test");
            http.User = new ClaimsPrincipal(identity);
        }

        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = http });
        services.AddModuleCatalog(config);
        services.AddNoForkPolicy(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_loads_matrix_and_two_packs_same_digest()
    {
        using var sp = BuildProvider(Environments.Development, new Dictionary<string, string?>
        {
            ["NoFork:EnforceOnStartup"] = "false"
        });
        var provider = sp.GetRequiredService<INoForkPolicyProvider>();
        Assert.Equal("STR-004", provider.Matrix.Card);
        Assert.Equal(2, provider.LoadedPacks.Count);
        Assert.All(provider.LoadedPacks, p => Assert.Equal(provider.ReleaseDigest, p.ReleaseDigest));
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "PROD-001" && b.Status == "Satisfied");
        Assert.Contains(provider.Matrix.Blockers, b => b.Id == "ONB-001" && b.Status == "Satisfied");
    }

    [Fact]
    public void Startup_fails_when_pack_digest_mismatches_matrix()
    {
        var badDir = Path.Combine(Path.GetTempPath(), "str004-bad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(badDir);
        try
        {
            var good = JsonSerializer.Deserialize<AgencyConfigPack>(
                System.IO.File.ReadAllText(Path.Combine(RepoRoot, "testdata", "str004", "agencies", "agency-a.json")))!;
            good.ReleaseDigest = "sha256:wrong";
            System.IO.File.WriteAllText(
                Path.Combine(badDir, "bad.json"),
                JsonSerializer.Serialize(good));

            using var sp = BuildProvider(Environments.Production, new Dictionary<string, string?>
            {
                ["NoFork:EnforceOnStartup"] = "true",
                ["NoFork:AgencyPacksDirectory"] = badDir
            });
            var validator = sp.GetServices<IHostedService>().OfType<NoForkStartupValidator>().Single();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                validator.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("STR-004", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(badDir, recursive: true);
        }
    }

    [Fact]
    public void Resolver_returns_pack_by_tenant()
    {
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["NoFork:EnforceOnStartup"] = "false"
        });
        var resolver = sp.GetRequiredService<IAgencyConfigResolver>();
        var a = resolver.TryResolve(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        Assert.NotNull(a);
        Assert.Equal("AGENCY-A", a!.AgencyId);
        Assert.Null(resolver.TryResolve(Guid.NewGuid()));
    }

    [Fact]
    public void Guard_hides_disabled_optional_for_agency_a()
    {
        var tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        using var sp = BuildProvider(Environments.Staging, new Dictionary<string, string?>
        {
            ["NoFork:EnforceOnStartup"] = "false",
            ["NoFork:ResolveAgencyPacks"] = "true"
        }, tenantA);
        var guard = sp.GetRequiredService<IModuleEntitlementGuard>();
        Assert.True(guard.IsModuleExposed("hr-core")); // mandatory
        Assert.False(guard.IsModuleExposed("hr-training")); // disabled for A
        Assert.True(guard.IsModuleExposed("compliance")); // enabled optional for A
    }

    [Fact]
    public void Runtime_validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "str004-nofork-runtime-validate.ps1");
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
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "ADR-STR-004-production-nofork-binding.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_004_ADOPTION_MINUTES.md")));
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "STR_004_INDEPENDENT_REVIEW_WAIVER.md")));
    }
}
