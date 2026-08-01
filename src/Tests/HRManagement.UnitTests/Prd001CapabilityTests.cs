using System.Text.Json;
using HRManagement.ServiceDefaults.NoFork;
using HRManagement.ServiceDefaults.ProductCapability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class Prd001CapabilityTests
{
    private static readonly string[] RequiredModules =
    [
        "hr-core", "hr-contracts", "hr-health", "hr-movements", "hr-training",
        "payroll", "idcard", "records-archive", "admin-logistics", "it-infra",
        "dev-analytics", "compliance", "auth-directory", "file-storage"
    ];

    private static readonly string[] RequiredArchetypes =
    [
        "central-ministry", "governorate", "autonomous-institution",
        "branch-office", "specialized-body"
    ];


    private static string RepoRoot
    {
        get
        {
            var map = RepoPaths.Resolve("docs", "prd-product", "capability-map.json");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(map)!, "..", ".."));
        }
    }

    private static ICapabilityRegistry BuildRegistry()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CapabilityProfile:CapabilityMapPath"] = Path.Combine(RepoRoot, "docs", "prd-product", "capability-map.json"),
                ["CapabilityProfile:ArchetypesPath"] = Path.Combine(RepoRoot, "docs", "prd-product", "agency-archetypes.json")
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
        services.AddProductCapability(config);
        return services.BuildServiceProvider().GetRequiredService<ICapabilityRegistry>();
    }

    [Fact]
    public void Capability_map_covers_all_fourteen_services()
    {
        var registry = BuildRegistry();
        Assert.Equal("PRD-001", registry.CapabilityMap.Card);
        Assert.Equal(14, registry.CapabilityMap.Services.Count);
        foreach (var id in RequiredModules)
            Assert.NotNull(registry.GetService(id));
    }

    [Fact]
    public void Each_archetype_has_at_least_one_mandatory_module()
    {
        var registry = BuildRegistry();
        foreach (var archetype in RequiredArchetypes)
        {
            var mandatory = RequiredModules.Count(m =>
                string.Equals(registry.GetClassification(archetype, m), "mandatory", StringComparison.OrdinalIgnoreCase));
            Assert.True(mandatory >= 1, $"{archetype} should have >=1 mandatory module");
        }
    }

    [Fact]
    public void Disabled_modules_blocked_by_registry()
    {
        var registry = BuildRegistry();
        Assert.False(registry.IsModuleEnabled("branch-office", "dev-analytics"));
        Assert.False(registry.IsModuleEnabled("specialized-body", "admin-logistics"));
        Assert.True(registry.IsModuleEnabled("central-ministry", "hr-core"));
    }

    [Fact]
    public void Every_capability_has_owner_role()
    {
        var registry = BuildRegistry();
        foreach (var service in registry.CapabilityMap.Services)
            Assert.False(string.IsNullOrWhiteSpace(service.OwnerRole), $"{service.ModuleId} missing owner");
    }

    [Fact]
    public void Prd001_docs_exist()
    {
        Assert.True(System.IO.File.Exists(Path.Combine(RepoRoot, "docs", "PRD_001_CAPABILITY_MAP.md")));
        Assert.True(System.IO.File.Exists(Path.Combine(RepoRoot, "docs", "PRD_001_COMPETITIVE_BASELINE.md")));
        Assert.True(System.IO.File.Exists(Path.Combine(RepoRoot, "docs", "prd-product", "agency-archetypes.json")));
        Assert.True(System.IO.File.Exists(Path.Combine(RepoRoot, "docs", "prd-product", "capability-map.json")));
        Assert.True(System.IO.File.Exists(Path.Combine(RepoRoot, "docs", "prd-product", "personas.json")));
        Assert.True(System.IO.File.Exists(Path.Combine(RepoRoot, "docs", "prd-product", "uat-walkthrough-template.md")));
    }

    [Fact]
    public void NoForkCapabilityValidation_rejects_disabled_optional_enable()
    {
        var registry = BuildRegistry();
        var pack = new AgencyConfigPack
        {
            AgencyId = "test-branch",
            EnabledOptionalModules = ["dev-analytics"]
        };
        var failures = NoForkCapabilityValidation.ValidateAgainstCapability(pack, registry, "branch-office");
        Assert.NotEmpty(failures);
    }

    [Fact]
    public void Capability_guard_middleware_resolves_archetype_from_header()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CapabilityRegistry.ArchetypeHeaderName] = "governorate";
        var resolved = CapabilityRegistry.ResolveArchetype(context, null);
        Assert.Equal("governorate", resolved);
    }

    [Fact]
    public void Archetypes_document_lists_five_archetypes()
    {
        var registry = BuildRegistry();
        Assert.Equal(5, registry.Archetypes.Archetypes.Count);
        foreach (var id in RequiredArchetypes)
            Assert.NotNull(registry.GetArchetype(id));
    }
}
