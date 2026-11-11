using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using HRManagement.TestSupport;
using HRManagement.TestSupport.Wiring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-49 (plan section 5, D-POL-1, D-POL-13): the five governed domains run in Shadow wherever production values apply: the
/// Production and Staging files of HR.Core and of the service that owns each domain, and the prod-like acceptance chain, which
/// repeats them as variables. The owner's value always equals HR.Core's (the client asks and the server derives the mode: a
/// difference is a mismatch on every call). No domain is in Enforce in any production file, no service sets a mode for a domain
/// it does not own, and none sets one for a domain out of the engine's scope.
/// </summary>
public sealed class Pol049PolicyModeConsistencyTests
{
    private const string HrCoreWeb = "src/Services/HRCore/HR.Core.Web";

    /// <summary>The web project and the compose service of each owner service of the catalog.</summary>
    private static readonly IReadOnlyDictionary<string, (string Project, string ComposeService)> Owners =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            [PolicyDomainCatalog.AdminLogisticsService] = ("src/Services/AdminLogistics/Admin.Logistics.Web", "admin-logistics-web"),
            [PolicyDomainCatalog.IdCardService] = ("src/Services/IDCard/IDCard.Web", "idcard-web"),
            [PolicyDomainCatalog.HrContractsService] = ("src/Services/HRContracts/HR.Contracts.Web", "hr-contracts-web")
        };

    /// <summary>
    /// The only files that may put a domain in Enforce: the lab acceptance stack (AC-10 and AC-11 need custody enforced) and the
    /// overlays that a single acceptance stage applies on purpose and removes with its stack. None of them is a production file.
    /// </summary>
    private static readonly string[] AcceptanceOnlyFilesThatMayEnforce =
    [
        "docker-compose.acceptance.yml",
        "docker-compose.acceptance-s2s42.yml",
        "docker-compose.acceptance-wf46.yml",
        "docker-compose.acceptance-pol49.yml"
    ];

    private static readonly string[] Governed = [.. PolicyDomainCatalog.Governed.Select(d => d.Domain).OrderBy(d => d, StringComparer.Ordinal)];

    private static string Read(string relative) =>
        global::System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, relative.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>The domain keys of the file's PolicyEngine section (not the engine's own settings), or null when it has none.</summary>
    private static Dictionary<string, string>? Modes(string relative)
    {
        var path = Path.Combine(RepoPaths.Root, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!global::System.IO.File.Exists(path))
            return null;

        using var json = JsonDocument.Parse(global::System.IO.File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!json.RootElement.TryGetProperty(PolicyModeResolver.SectionName, out var section))
            return null;

        return section.EnumerateObject()
            .Where(p => !PolicyEngineConfigurationValidator.SettingsSections.Contains(p.Name)
                        && !string.Equals(p.Name, PolicyModeResolver.FailureModeKey, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
    }

    /// <summary>service → domain → mode, from the PolicyEngine__ variables of a merged compose chain.</summary>
    private static Dictionary<string, Dictionary<string, string>> ComposeModes(IEnumerable<string> files) =>
        ComposeEffectiveEnvironment.Load(RepoPaths.Root, files)
            .ToDictionary(
                service => service.Key,
                service => service.Value
                    .Where(kv => kv.Key.StartsWith("PolicyEngine__", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(kv => kv.Key["PolicyEngine__".Length..], kv => kv.Value, StringComparer.Ordinal),
                StringComparer.Ordinal);

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void HR_Core_and_each_owner_service_set_the_same_Shadow_for_every_governed_domain(string environment)
    {
        var core = Modes($"{HrCoreWeb}/appsettings.{environment}.json");
        Assert.NotNull(core);
        Assert.Equal(Governed, core!.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.All(core.Values, mode => Assert.Equal("Shadow", mode));

        foreach (var descriptor in PolicyDomainCatalog.Governed.Where(d => d.OwnerClientId is not null))
        {
            var (project, _) = Owners[descriptor.OwnerService];
            var owner = Modes($"{project}/appsettings.{environment}.json");
            Assert.True(owner is not null, $"{project}/appsettings.{environment}.json has no PolicyEngine section (or does not exist).");
            // The owner sets its own domain, to HR.Core's value, and nothing else.
            Assert.Equal([descriptor.Domain], owner!.Keys);
            Assert.Equal(core[descriptor.Domain], owner[descriptor.Domain]);
        }

        // The governed domains with no owner client run inside HR.Core: no other file is concerned.
        Assert.Equal(3, PolicyDomainCatalog.Governed.Count(d => d.OwnerClientId is not null));
        Assert.Equal(Owners.Keys.OrderBy(k => k, StringComparer.Ordinal),
            PolicyDomainCatalog.Governed.Where(d => d.OwnerClientId is not null).Select(d => d.OwnerService).OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void The_prod_like_chain_repeats_the_production_values_on_the_four_services()
    {
        var modes = ComposeModes(WiringChecks.ComposeChains(RepoPaths.Root)["prodlike"]);

        var core = modes["hr-core-web"];
        Assert.Equal(Governed, core.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.All(core.Values, mode => Assert.Equal("Shadow", mode));
        Assert.Equal(Modes($"{HrCoreWeb}/appsettings.Production.json"), core);

        foreach (var descriptor in PolicyDomainCatalog.Governed.Where(d => d.OwnerClientId is not null))
        {
            var (project, composeService) = Owners[descriptor.OwnerService];
            var owner = modes[composeService];
            Assert.Equal([descriptor.Domain], owner.Keys);
            Assert.Equal(core[descriptor.Domain], owner[descriptor.Domain]);
            Assert.Equal(Modes($"{project}/appsettings.Production.json"), owner);
        }

        // Every other service of the chain has no PolicyEngine variable.
        var others = modes.Where(m => m.Key != "hr-core-web" && !Owners.Values.Any(o => o.ComposeService == m.Key) && m.Value.Count > 0).Select(m => m.Key);
        Assert.Empty(others);

        // The lab file, which is part of this chain, enforces custody on HR.Core for AC-10 and AC-11: the prod-like file wins.
        var lab = ComposeModes(WiringChecks.ComposeChains(RepoPaths.Root)["prodlike"].Where(f => f != "docker-compose.acceptance-prodlike.yml"));
        Assert.Equal("Enforce", lab["hr-core-web"][PolicyDomains.AssetsCustody]);
    }

    [Theory]
    [InlineData("production")]
    [InlineData("single-host")]
    public void The_production_chains_set_no_mode_in_compose_so_the_Production_files_decide(string chain)
    {
        var modes = ComposeModes(WiringChecks.ComposeChains(RepoPaths.Root)[chain]);

        Assert.DoesNotContain(modes, m => m.Value.Count > 0);
    }

    [Fact]
    public void No_shipped_configuration_puts_a_domain_in_Enforce_or_names_a_domain_out_of_scope()
    {
        var root = RepoPaths.Root;
        var outOfScope = PolicyDomainCatalog.OutOfScopeDomains.Select(d => d.Domain).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Every appsettings file of every host.
        var settings = Directory.EnumerateFiles(Path.Combine(root, "src"), "appsettings*.json", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal) && !f.StartsWith("src/Tests/", StringComparison.Ordinal))
            .ToList();
        Assert.True(settings.Count >= 45, $"only {settings.Count} appsettings files found");
        var withModes = new List<string>();
        foreach (var file in settings)
        {
            var modes = Modes(file);
            if (modes is null)
                continue;

            withModes.Add(file);
            foreach (var (key, mode) in modes.Where(m => !string.Equals(m.Key, PolicyModeResolver.DefaultModeKey, StringComparison.OrdinalIgnoreCase)))
            {
                Assert.True(PolicyDomainCatalog.IsGoverned(key), $"{file}: PolicyEngine:{key} is not a governed domain.");
                Assert.False(outOfScope.Contains(key), $"{file}: PolicyEngine:{key} is out of the engine's scope.");
                Assert.True(mode is "Shadow" or "Off", $"{file}: PolicyEngine:{key} is {mode}; no production file enforces a domain (D-POL-13).");
            }
        }

        // Exactly the files of HR.Core and the three owners, Production and Staging.
        var expected = new[] { HrCoreWeb }.Concat(Owners.Values.Select(o => o.Project))
            .SelectMany(p => new[] { $"{p}/appsettings.Production.json", $"{p}/appsettings.Staging.json" })
            .OrderBy(f => f, StringComparer.Ordinal);
        Assert.Equal(expected, withModes.OrderBy(f => f, StringComparer.Ordinal));

        // Compose and deployment files: a PolicyEngine variable with Enforce only in the acceptance-only files, and none for
        // a domain out of scope anywhere.
        var deployment = Directory.EnumerateFiles(root, "docker-compose*.yml", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "deploy"), "*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".yml", StringComparison.Ordinal) || f.EndsWith(".yaml", StringComparison.Ordinal)
                        || f.EndsWith(".json", StringComparison.Ordinal) || Path.GetFileName(f).StartsWith(".env", StringComparison.Ordinal)
                        || f.EndsWith(".example", StringComparison.Ordinal) || f.EndsWith(".sh", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .ToList();
        Assert.Contains("docker-compose.production.yml", deployment);
        Assert.Contains("deploy/k8s/base/configmap-app.yaml", deployment);
        var variable = new Regex(@"PolicyEngine(?:__|:)(?<domain>[A-Za-z]+\.[A-Za-z]+)""?\s*[:=]\s*""?(?<mode>[^""\s#]+)", RegexOptions.CultureInvariant);
        foreach (var file in deployment)
        {
            foreach (System.Text.RegularExpressions.Match match in variable.Matches(Read(file)))
            {
                var domain = match.Groups["domain"].Value;
                var mode = match.Groups["mode"].Value;
                Assert.False(outOfScope.Contains(domain), $"{file}: a mode for {domain}, which is out of the engine's scope.");
                if (!AcceptanceOnlyFilesThatMayEnforce.Contains(file))
                    Assert.True(mode is "Shadow" or "Off", $"{file}: {domain} is {mode}; only an acceptance-only file may enforce a domain.");
            }
        }

        Assert.All(AcceptanceOnlyFilesThatMayEnforce, f => Assert.StartsWith("docker-compose.acceptance", f, StringComparison.Ordinal));
        Assert.All(AcceptanceOnlyFilesThatMayEnforce, f => Assert.DoesNotContain(f, WiringChecks.ComposeChains(root)["production"]));
        Assert.All(AcceptanceOnlyFilesThatMayEnforce.Where(f => f != "docker-compose.acceptance.yml"), f => Assert.DoesNotContain(f, WiringChecks.ComposeChains(root)["prodlike"]));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void The_start_up_check_accepts_these_files_and_an_owner_still_needs_the_address_of_HR_Core(string environment)
    {
        var host = Mock.Of<IHostEnvironment>(e => e.EnvironmentName == environment);

        // HR.Core: no remote client.
        var core = new ConfigurationBuilder().AddJsonFile(Path.Combine(RepoPaths.Root, HrCoreWeb.Replace('/', Path.DirectorySeparatorChar), $"appsettings.{environment}.json")).Build();
        Assert.Empty(PolicyEngineConfigurationValidator.Evaluate(core, host, new PolicyEngineValidationOptions()));

        foreach (var (project, _) in Owners.Values)
        {
            var file = Path.Combine(RepoPaths.Root, project.Replace('/', Path.DirectorySeparatorChar), $"appsettings.{environment}.json");
            var remote = new PolicyEngineValidationOptions { UsesRemoteClient = true };

            // As deployed: the wiring gives every owner the address (deploy/wiring/service-wiring.json).
            var wired = new ConfigurationBuilder().AddJsonFile(file)
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Integration:HrCoreBaseUrl"] = "http://hr-core-web:8080" }).Build();
            Assert.Empty(PolicyEngineConfigurationValidator.Evaluate(wired, host, remote));

            // Without it the service does not start: a domain in Shadow is a call to HR.Core.
            var unwired = new ConfigurationBuilder().AddJsonFile(file).Build();
            var failure = Assert.Single(PolicyEngineConfigurationValidator.Evaluate(unwired, host, remote));
            Assert.Contains("Integration:HrCoreBaseUrl", failure, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_resolver_reads_Shadow_from_the_Production_files_and_Off_where_no_file_applies()
    {
        foreach (var descriptor in PolicyDomainCatalog.Governed)
        {
            var project = descriptor.OwnerClientId is null ? HrCoreWeb : Owners[descriptor.OwnerService].Project;
            foreach (var environment in new[] { "Production", "Staging" })
            {
                var configuration = new ConfigurationBuilder()
                    .AddJsonFile(Path.Combine(RepoPaths.Root, project.Replace('/', Path.DirectorySeparatorChar), "appsettings.json"))
                    .AddJsonFile(Path.Combine(RepoPaths.Root, project.Replace('/', Path.DirectorySeparatorChar), $"appsettings.{environment}.json"))
                    .Build();
                Assert.Equal(PolicyEnforcementMode.Shadow, new PolicyModeResolver(configuration).Resolve(descriptor.Domain));
            }

            // Development and the lab stack load the base file only: Off, the engine has no say.
            var baseOnly = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(RepoPaths.Root, project.Replace('/', Path.DirectorySeparatorChar), "appsettings.json")).Build();
            Assert.Equal(PolicyEnforcementMode.Off, new PolicyModeResolver(baseOnly).Resolve(descriptor.Domain));
        }
    }

    [Fact]
    public void What_hcm025_validate_pins_still_holds()
    {
        // scripts/hcm025-validate.ps1 reads these two values and this exact line.
        foreach (var environment in new[] { "Production", "Staging" })
            Assert.Equal("Shadow", Modes($"{HrCoreWeb}/appsettings.{environment}.json")![PolicyDomains.CoreRecruitment]);
        Assert.Contains("      PolicyEngine__Core.Recruitment: \"Shadow\"", Read("docker-compose.acceptance-prodlike.yml"), StringComparison.Ordinal);
    }
}
