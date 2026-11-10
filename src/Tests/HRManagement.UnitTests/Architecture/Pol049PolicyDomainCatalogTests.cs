using System.Reflection;
using System.Text;
using HR.Core.Infrastructure.Services.Policy;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Policy;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-49 (G-POL-02, D-POL-1): one catalog says what the policy engine governs, and the code agrees with it. Every governed
/// domain has a proved call site; every engine call in the product is a listed call site; nothing outside the engine names an
/// out-of-scope domain; only the owner of a governed domain registers the enforce client; the owners, the trigger allow-list,
/// the policy page and docs/POLICY_DOMAIN_REGISTRY.md hold no list of their own.
/// </summary>
public sealed class Pol049PolicyDomainCatalogTests
{
    /// <summary>D-POL-1, as the owner accepted it on 2026-10-31. A change here is an owner decision, not a refactoring.</summary>
    private static readonly string[] GovernedByDecision =
    [
        PolicyDomains.CoreRecruitment, PolicyDomains.CoreEstablishment, PolicyDomains.AssetsCustody,
        PolicyDomains.IDCardLifecycle, PolicyDomains.ContractsLeave
    ];

    /// <summary>What a call site of each governed domain names besides the engine call: its facts builder, or the domain itself.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> DomainMarkers = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [PolicyDomains.AssetsCustody] = ["AssetPolicyFactsBuilder."],
        [PolicyDomains.IDCardLifecycle] = ["PolicyDomainFactsBuilders.ForIDCardRequest("],
        [PolicyDomains.ContractsLeave] = ["PolicyDomainFactsBuilders.ForLeaveRequest("],
        [PolicyDomains.CoreRecruitment] = ["PolicyDomains.CoreRecruitment"],
        [PolicyDomains.CoreEstablishment] = ["PolicyDomains.CoreEstablishment"]
    };

    /// <summary>
    /// An engine call: the remote client or HR.Core's orchestrator (<c>.EnforceAsync(</c>), or HR.Core's approval start, which
    /// asks the engine through <c>ApprovalStartCoordinator</c> for the domain the request names.
    /// </summary>
    private static readonly string[] EngineCalls = [".EnforceAsync(", "new ApprovalStartRequest("];

    /// <summary>The engine itself: it holds the calls every call site goes through, and names no domain of its own.</summary>
    private static readonly string[] EngineInfrastructure =
    [
        "src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyOrchestrator.cs",
        "src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/ApprovalStartCoordinator.cs",
        "src/Shared/HRManagement.ServiceDefaults/Policy/PolicyEnforceClient.cs"
    ];

    /// <summary>
    /// The only product files that may name a domain the catalog puts out of scope: the list of names, the catalog, and the
    /// recorded seed change sets (the baseline's Archive.Retention part and the retirement set never change once applied).
    /// PLAN-49 P1: the fresh seed and the bindings still name them until P2 takes them out, and the advance builder until P3.
    /// </summary>
    private static readonly string[] MayNameOutOfScopeDomains =
    [
        "src/Shared/HRManagement.SharedKernel/Policy/PolicyDomains.cs",
        "src/Shared/HRManagement.SharedKernel/Policy/PolicyDomainCatalog.cs",
        "src/Services/HRCore/HR.Core.Infrastructure/Data/PolicySeedChangeSets.cs",
        "src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineDomainSeeds.cs",
        "src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineSeeder.cs",
        "src/Shared/HRManagement.ServiceDefaults/Policy/PolicyDomainFactsBuilders.cs"
    ];

    private const string RegistryDocument = "docs/POLICY_DOMAIN_REGISTRY.md";
    private const string WriteRegistryVariable = "POL049_WRITE_REGISTRY";

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !global::System.IO.File.Exists(Path.Combine(dir.FullName, "HRManagement.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }

    private static string Read(string relative) => global::System.IO.File.ReadAllText(Path.Combine(RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>Every product source file (services, shared libraries, gateway), without build output and migrations.</summary>
    private static IEnumerable<(string Relative, string Text)> ProductSources()
    {
        var root = RepoRoot;
        foreach (var top in new[] { "src/Services", "src/Shared", "src/Gateway", "src/HR.Core.Domain" })
        {
            var directory = Path.Combine(root, top.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
            {
                if (!file.EndsWith(".cs", StringComparison.Ordinal) && !file.EndsWith(".cshtml", StringComparison.Ordinal))
                    continue;

                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal)
                    || relative.Contains("/Migrations/", StringComparison.Ordinal))
                    continue;

                yield return (relative, global::System.IO.File.ReadAllText(file));
            }
        }
    }

    /// <summary>The constants of <see cref="PolicyDomains"/>: constant name → domain name.</summary>
    private static IReadOnlyDictionary<string, string> DomainConstants() =>
        typeof(PolicyDomains).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!, StringComparer.Ordinal);

    [Fact]
    public void The_catalog_describes_every_domain_once_as_the_owner_decided()
    {
        var domains = PolicyDomainCatalog.All.Select(d => d.Domain).ToList();

        Assert.Equal(domains.Count, domains.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(PolicyDomains.All.OrderBy(d => d, StringComparer.Ordinal), domains.OrderBy(d => d, StringComparer.Ordinal));
        Assert.Equal(DomainConstants().Values.OrderBy(d => d, StringComparer.Ordinal), domains.OrderBy(d => d, StringComparer.Ordinal));
        Assert.Equal(
            GovernedByDecision.OrderBy(d => d, StringComparer.Ordinal),
            PolicyDomainCatalog.Governed.Select(d => d.Domain).OrderBy(d => d, StringComparer.Ordinal));
        Assert.Equal(15, PolicyDomainCatalog.OutOfScopeDomains.Count());

        foreach (var domain in PolicyDomains.All)
        {
            Assert.Equal(GovernedByDecision.Contains(domain), PolicyDomainCatalog.IsGoverned(domain));
            Assert.Equal(!GovernedByDecision.Contains(domain), PolicyDomainCatalog.IsOutOfScope(domain));
        }

        // A name that is no domain, and a known name in another case, are outside the engine: neither governed nor "out of scope".
        foreach (var unknown in new[] { "Unknown.Domain", "assets.custody", "", null })
        {
            Assert.False(PolicyDomainCatalog.IsGoverned(unknown));
            Assert.False(PolicyDomainCatalog.IsOutOfScope(unknown));
            Assert.Null(PolicyDomainCatalog.Find(unknown));
        }

        // A configuration key ignores case, so the start-up check finds the descriptor either way.
        Assert.Equal(PolicyDomains.ITTickets, PolicyDomainCatalog.FindByConfigurationKey("it.tickets")!.Domain);
    }

    [Fact]
    public void A_governed_domain_says_what_it_does_and_an_out_of_scope_one_says_only_why()
    {
        foreach (var d in PolicyDomainCatalog.Governed)
        {
            Assert.Null(d.OutOfScopeReasonAr);
            Assert.NotEmpty(d.SubjectActions);
            Assert.All(d.SubjectActions, kv => Assert.NotEmpty(kv.Value));
            Assert.NotEmpty(d.CallSiteFiles);
            Assert.Contains(PolicyEffect.Allow, d.AllowedEffects);
            Assert.Contains(PolicyEffect.Deny, d.AllowedEffects);

            // A trigger is given to an entity type the domain has, and only where the effect is allowed; and the reverse.
            Assert.All(d.WorkflowTriggers.Keys, type => Assert.Contains(type, d.SubjectActions.Keys));
            Assert.All(d.WorkflowTriggers.Values, triggers => Assert.NotEmpty(triggers));
            Assert.Equal(d.AllowedEffects.Contains(PolicyEffect.TriggerWorkflow), d.WorkflowTriggers.Count > 0);
            Assert.Equal(d.AllowedEffects.Contains(PolicyEffect.SetField), d.SetFields.Count > 0);

            if (d.Domain.StartsWith("Core.", StringComparison.Ordinal))
            {
                // The domain runs inside HR.Core: no client reaches it over HTTP, and HR.Core's own approval start starts its workflows.
                Assert.Equal(PolicyDomainCatalog.HrCoreService, d.OwnerService);
                Assert.Null(d.OwnerClientId);
                Assert.All(d.WorkflowTriggers.Values.SelectMany(t => t), t => Assert.Equal(PolicyWorkflowStartPath.HrCoreCoordinator, t.StartedBy));
            }
            else
            {
                Assert.Contains(d.OwnerClientId!, ServiceClientIds.All);
                Assert.All(d.WorkflowTriggers.Values.SelectMany(t => t), t => Assert.Equal(PolicyWorkflowStartPath.OwnerApprovalContract, t.StartedBy));
            }
        }

        foreach (var d in PolicyDomainCatalog.OutOfScopeDomains)
        {
            Assert.False(string.IsNullOrWhiteSpace(d.OutOfScopeReasonAr), d.Domain);
            Assert.Matches("[؀-ۿ]", d.OutOfScopeReasonAr!);
            Assert.False(string.IsNullOrWhiteSpace(d.OwnerService), d.Domain);
            Assert.Null(d.OwnerClientId);
            Assert.Empty(d.SubjectActions);
            Assert.Empty(d.AllowedEffects);
            Assert.Empty(d.SetFields);
            Assert.Empty(d.WorkflowTriggers);
            Assert.Empty(d.CallSiteFiles);
        }
    }

    [Fact]
    public void Every_call_site_the_catalog_lists_asks_the_engine_for_its_domain()
    {
        foreach (var d in PolicyDomainCatalog.Governed)
        {
            foreach (var file in d.CallSiteFiles)
            {
                Assert.True(global::System.IO.File.Exists(Path.Combine(RepoRoot, file.Replace('/', Path.DirectorySeparatorChar))), $"{d.Domain}: {file} does not exist.");
                var text = Read(file);
                Assert.True(EngineCalls.Any(call => text.Contains(call, StringComparison.Ordinal)),
                    $"{d.Domain}: {file} is listed as a call site and holds no engine call ({string.Join(" or ", EngineCalls)}).");
                Assert.True(DomainMarkers[d.Domain].Any(marker => text.Contains(marker, StringComparison.Ordinal)),
                    $"{d.Domain}: {file} is listed as a call site and names neither the domain nor its facts builder.");
            }
        }
    }

    [Fact]
    public void Every_engine_call_in_the_product_is_a_listed_call_site()
    {
        var listed = PolicyDomainCatalog.Governed.SelectMany(d => d.CallSiteFiles).ToHashSet(StringComparer.Ordinal);
        var calling = ProductSources()
            .Where(f => EngineCalls.Any(call => f.Text.Contains(call, StringComparison.Ordinal)))
            .Select(f => f.Relative)
            .Where(f => !EngineInfrastructure.Contains(f))
            .ToList();

        var unlisted = calling.Where(f => !listed.Contains(f)).ToList();
        Assert.True(unlisted.Count == 0,
            "These files ask the policy engine and are not a call site of any governed domain in PolicyDomainCatalog: " + string.Join(", ", unlisted));
        var silent = listed.Where(f => !calling.Contains(f)).ToList();
        Assert.True(silent.Count == 0, "Listed call sites that hold no engine call: " + string.Join(", ", silent));
        Assert.All(EngineInfrastructure, f => Assert.True(global::System.IO.File.Exists(Path.Combine(RepoRoot, f.Replace('/', Path.DirectorySeparatorChar))), f));
    }

    [Fact]
    public void Nothing_outside_the_engine_names_a_domain_out_of_its_scope()
    {
        var constants = DomainConstants();
        var outOfScope = PolicyDomainCatalog.OutOfScopeDomains.Select(d => d.Domain).ToHashSet(StringComparer.Ordinal);
        var needles = constants
            .Where(c => outOfScope.Contains(c.Value))
            .SelectMany(c => new[] { $"PolicyDomains.{c.Key}", $"\"{c.Value}\"", $"PolicyEngine:{c.Value}", $"PolicyEngine__{c.Value}" })
            .ToList();
        Assert.Equal(15 * 4, needles.Count);

        var offenders = ProductSources()
            .Where(f => !MayNameOutOfScopeDomains.Contains(f.Relative))
            .SelectMany(f => needles.Where(n => f.Text.Contains(n, StringComparison.Ordinal)).Select(n => $"{f.Relative}: {n}"))
            .ToList();

        Assert.True(offenders.Count == 0,
            "A domain the catalog puts out of scope is named outside the engine's own files: " + string.Join("; ", offenders));
        Assert.All(MayNameOutOfScopeDomains, f => Assert.True(global::System.IO.File.Exists(Path.Combine(RepoRoot, f.Replace('/', Path.DirectorySeparatorChar))), f));
    }

    [Fact]
    public void The_guard_finds_an_out_of_scope_name_where_one_is_written()
    {
        // The guard's own negative: the file that defines the names is found when it is not on the list.
        var constants = DomainConstants();
        var text = Read("src/Shared/HRManagement.SharedKernel/Policy/PolicyDomains.cs");
        Assert.Contains($"\"{PolicyDomains.ITTickets}\"", text);
        Assert.Contains(constants, c => c.Value == PolicyDomains.ITTickets && c.Key == nameof(PolicyDomains.ITTickets));
        Assert.Contains(ProductSources(), f => f.Relative == "src/Shared/HRManagement.SharedKernel/Policy/PolicyDomains.cs");
    }

    [Fact]
    public void Only_the_owner_of_a_governed_domain_registers_the_enforce_client()
    {
        // Admin.Logistics → AdminLogistics, HR.Contracts → HRContracts: the folder of a service is its name without the dots.
        var expected = PolicyDomainCatalog.Governed
            .Where(d => d.OwnerClientId is not null)
            .Select(d => d.OwnerService.Replace(".", string.Empty, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(3, expected.Count);

        var registering = ProductSources()
            .Where(f => f.Relative.StartsWith("src/Services/", StringComparison.Ordinal) && f.Text.Contains(".AddHrPolicyEnforceClient(", StringComparison.Ordinal))
            .Select(f => f.Relative.Split('/')[2])
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(expected.OrderBy(s => s, StringComparer.Ordinal), registering.OrderBy(s => s, StringComparer.Ordinal));
        Assert.DoesNotContain("HRCore", registering);
        Assert.DoesNotContain(ProductSources(), f => f.Relative.StartsWith("src/Gateway/", StringComparison.Ordinal)
            && f.Text.Contains(".AddHrPolicyEnforceClient(", StringComparison.Ordinal));

        // And each registering service calls it: its domain's call sites are its own files, and they inject the client.
        foreach (var d in PolicyDomainCatalog.Governed.Where(d => d.OwnerClientId is not null))
        {
            var folder = "src/Services/" + d.OwnerService.Replace(".", string.Empty, StringComparison.Ordinal) + "/";
            Assert.All(d.CallSiteFiles, file => Assert.StartsWith(folder, file, StringComparison.Ordinal));
            Assert.Contains(d.CallSiteFiles, file => Read(file).Contains("IPolicyEnforceClient", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void The_owners_and_the_trigger_allow_list_are_read_from_the_catalog()
    {
        var owned = PolicyDomainCatalog.Governed.Where(d => d.OwnerClientId is not null).ToList();
        Assert.Equal(owned.Select(d => d.Domain).OrderBy(d => d, StringComparer.Ordinal), PolicyDomainOwners.OwnedDomains.OrderBy(d => d, StringComparer.Ordinal));

        foreach (var d in PolicyDomainCatalog.All)
        {
            foreach (var client in ServiceClientIds.All)
                Assert.Equal(d.IsGoverned && d.OwnerClientId == client, PolicyDomainOwners.Owns(client, d.Domain));
            Assert.False(PolicyDomainOwners.Owns(null, d.Domain));
        }

        Assert.All(ServiceClientIds.All, client =>
            Assert.Equal(owned.Any(d => d.OwnerClientId == client), PolicyDomainOwners.IsOwnerOfAnyDomain(client)));

        // PLAN-43's meaning holds: the internal endpoint starts nothing, because the catalog marks no trigger for it.
        Assert.Empty(PolicyWorkflowTriggerAllowlist.Triples);
        foreach (var d in PolicyDomainCatalog.Governed)
        {
            foreach (var (type, triggers) in d.WorkflowTriggers)
                Assert.All(triggers, t => Assert.False(PolicyWorkflowTriggerAllowlist.Allows(d.Domain, type, t.WorkflowCode)));
        }

        // Neither file holds a domain, a client or a workflow code of its own.
        foreach (var file in new[]
                 {
                     "src/Shared/HRManagement.SharedKernel/Policy/PolicyDomainOwners.cs",
                     "src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyWorkflowTriggerAllowlist.cs"
                 })
        {
            var text = Read(file);
            Assert.Contains("PolicyDomainCatalog", text);
            Assert.DoesNotContain("PolicyDomains.", text);
            Assert.DoesNotContain("\"svc-", text);
            Assert.DoesNotContain("-APPROVAL\"", text);
        }
    }

    [Fact]
    public void The_policy_page_reads_the_catalog_and_keeps_no_list_of_its_own()
    {
        var controller = Read("src/Services/HRCore/HR.Core.Web/Controllers/PolicyTemplatesController.cs");
        var view = Read("src/Services/HRCore/HR.Core.Web/Views/PolicyTemplates/Index.cshtml");

        Assert.DoesNotContain("PolicyDomainRegistry", controller);
        Assert.Contains("PolicyDomainCatalog.All", controller);
        Assert.Contains("PolicyDomainRow", view);
        Assert.Contains("policy-domain-catalog", view);
        foreach (var constant in DomainConstants())
        {
            Assert.DoesNotContain($"PolicyDomains.{constant.Key}", controller);
            Assert.DoesNotContain($"\"{constant.Value}\"", controller);
            Assert.DoesNotContain(constant.Value, view);
        }

        // Nothing else in the product defines a registry of domains.
        Assert.DoesNotContain(ProductSources(), f => f.Text.Contains("class PolicyDomainRegistry", StringComparison.Ordinal));
        Assert.DoesNotContain(ProductSources(), f => f.Text.Contains("AllowAllPolicyDomainCapabilities", StringComparison.Ordinal));
        Assert.DoesNotContain(ProductSources(), f => f.Text.Contains("PolicyDomainGateway", StringComparison.Ordinal));
    }

    [Fact]
    public void The_registry_document_is_the_catalog()
    {
        var expected = RenderRegistry();
        var path = Path.Combine(RepoRoot, RegistryDocument.Replace('/', Path.DirectorySeparatorChar));

        // Written again on purpose only: run this test once with POL049_WRITE_REGISTRY=1 after a catalog change.
        if (Environment.GetEnvironmentVariable(WriteRegistryVariable) == "1")
            global::System.IO.File.WriteAllText(path, expected, new UTF8Encoding(false));

        var actual = global::System.IO.File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.True(expected == actual,
            $"{RegistryDocument} differs from PolicyDomainCatalog. Run this test once with {WriteRegistryVariable}=1 to write it again, and commit it.");
    }

    /// <summary>docs/POLICY_DOMAIN_REGISTRY.md, from the catalog and nothing else.</summary>
    private static string RenderRegistry()
    {
        static string Code(string value) => "`" + value + "`";
        static string OrDash(IEnumerable<string> values)
        {
            var list = values.ToList();
            return list.Count == 0 ? "-" : string.Join("; ", list);
        }

        var governed = PolicyDomainCatalog.Governed.ToList();
        var outOfScope = PolicyDomainCatalog.OutOfScopeDomains.ToList();
        var lines = new List<string>
        {
            "# Policy Domain Registry",
            "",
            "Generated from `PolicyDomainCatalog` (`src/Shared/HRManagement.SharedKernel/Policy/PolicyDomainCatalog.cs`), the one source",
            "of truth about the domains of the policy engine (PLAN-49, G-POL-02, owner decision D-POL-1). **Do not edit by hand.**",
            "`Pol049PolicyDomainCatalogTests` compares this file with the catalog; after a catalog change, run that test once with",
            $"`{WriteRegistryVariable}=1` to write it again. The validator, the engine, the internal enforce endpoint, the start-up check and",
            "the policy page (`/ui/hr-core/PolicyTemplates`) read the same catalog.",
            "",
            $"## Governed domains ({governed.Count})",
            "",
            "Each has a proved call site and seeded rules, and runs in Shadow in the Production and Staging files: the engine records",
            "its decision in the ledger and changes no outcome (D-POL-13: no domain is in Enforce). A rule may use only the effects, the",
            "SetField fields and the workflow codes listed here, and a condition on `action` may name only the actions listed here.",
            "",
            "| Domain | Owner service | Owner client | Entity types and actions | Effects | SetField fields | Workflow triggers | Call sites |",
            "|---|---|---|---|---|---|---|---|"
        };

        foreach (var d in governed)
        {
            var actions = d.SubjectActions.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value.OrderBy(a => a, StringComparer.Ordinal))}");
            var triggers = d.WorkflowTriggers.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value.OrderBy(t => t.WorkflowCode, StringComparer.Ordinal).Select(t => $"{t.WorkflowCode} (started by {t.StartedBy})"))}");
            lines.Add(string.Join(" | ", new[]
            {
                "| " + Code(d.Domain),
                d.OwnerService,
                d.OwnerClientId is null ? "- (inside HR.Core)" : Code(d.OwnerClientId),
                OrDash(actions),
                string.Join(", ", d.AllowedEffects.OrderBy(e => (int)e).Select(e => e.ToString())),
                OrDash(d.SetFields.OrderBy(f => f, StringComparer.Ordinal)),
                OrDash(triggers),
                string.Join("<br>", d.CallSiteFiles.Select(Code)) + " |"
            }));
        }

        lines.AddRange(
        [
            "",
            $"## Out of scope ({outOfScope.Count})",
            "",
            "No call site. The national template is retired by a final version with no rules (seed change set `POL-049-SCOPE-RETIRE`),",
            "the mode is Off whatever the configuration says (a key that is not Off stops the service at start-up), the engine answers",
            "a neutral decision with no ledger row, and a draft, a rule or a publication is refused. Bringing a domain back means a",
            "descriptor in the catalog, a call site and a seed change set.",
            "",
            "| Domain | Service | Reason |",
            "|---|---|---|"
        ]);
        lines.AddRange(outOfScope.Select(d => $"| {Code(d.Domain)} | {d.OwnerService} | {d.OutOfScopeReasonAr} |"));
        lines.AddRange(
        [
            "",
            "Local rule files of every service, and whether the engine governs their subject: `docs/policy-rules-inventory.csv`",
            "(written by `scripts/policy-inventory.ps1`).",
            ""
        ]);

        return string.Join("\n", lines);
    }
}
