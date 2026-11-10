using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Web.Controllers;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Integration;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using static HRManagement.UnitTests.Ten048PolicyHarness;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-49 (G-POL-02, D-POL-1): a domain the catalog puts out of scope is outside the engine everywhere. Its mode is Off whatever
/// the configuration says; a key that is not Off stops any service at start-up; the engine answers a neutral decision with no
/// ledger row and no workflow; and nothing is drafted, added or published in it, on the page, in the API or in the services —
/// except its retirement: the final version with no rules, by a seed change set.
/// </summary>
public sealed class Pol049OutOfScopeTests
{
    private const string Retired = PolicyDomains.ITTickets;

    public static TheoryData<string> OutOfScopeDomains() => [.. PolicyDomainCatalog.OutOfScopeDomains.Select(d => d.Domain)];

    public static TheoryData<string> GovernedDomains() => [.. PolicyDomainCatalog.Governed.Select(d => d.Domain)];

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

    private static IHostEnvironment Environment(string name) => Mock.Of<IHostEnvironment>(e => e.EnvironmentName == name);

    // ---- the mode ----

    [Theory, MemberData(nameof(OutOfScopeDomains))]
    public void The_mode_of_an_out_of_scope_domain_is_off_whatever_the_key_and_the_default_say(string domain)
    {
        foreach (var value in new[] { "Shadow", "Enforce", "Off", "Nope" })
        {
            var resolver = new PolicyModeResolver(Config(($"PolicyEngine:{domain}", value), ("PolicyEngine:DefaultMode", "Enforce")));
            Assert.Equal(PolicyEnforcementMode.Off, resolver.Resolve(domain));
        }

        Assert.Equal(PolicyEnforcementMode.Off, new PolicyModeResolver(Config(("PolicyEngine:DefaultMode", "Shadow"))).Resolve(domain));
    }

    [Theory, MemberData(nameof(GovernedDomains))]
    public void The_mode_of_a_governed_domain_still_comes_from_its_key_then_the_default(string domain)
    {
        Assert.Equal(PolicyEnforcementMode.Shadow, new PolicyModeResolver(Config(($"PolicyEngine:{domain}", "Shadow"))).Resolve(domain));
        Assert.Equal(PolicyEnforcementMode.Enforce, new PolicyModeResolver(Config(("PolicyEngine:DefaultMode", "Enforce"))).Resolve(domain));
        Assert.Equal(PolicyEnforcementMode.Off, new PolicyModeResolver(Config()).Resolve(domain));
        Assert.Throws<InvalidOperationException>(() => new PolicyModeResolver(Config(($"PolicyEngine:{domain}", "Nope"))).Resolve(domain));
    }

    [Fact]
    public void A_name_that_is_no_domain_is_off_even_with_a_default()
    {
        var resolver = new PolicyModeResolver(Config(("PolicyEngine:DefaultMode", "Enforce"), ("PolicyEngine:Unknown.Domain", "Enforce")));

        Assert.Equal(PolicyEnforcementMode.Off, resolver.Resolve("Unknown.Domain"));
        Assert.Equal(PolicyEnforcementMode.Off, resolver.Resolve("assets.custody"));
    }

    // ---- the start-up check ----

    [Theory]
    [InlineData("Development")]
    [InlineData("Acceptance")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void A_key_of_an_out_of_scope_domain_that_is_not_off_stops_the_start_and_names_the_key_only(string environment)
    {
        foreach (var domain in PolicyDomainCatalog.OutOfScopeDomains.Select(d => d.Domain))
        {
            foreach (var value in new[] { "Shadow", "Enforce" })
            {
                var failures = PolicyEngineConfigurationValidator.Evaluate(
                    Config(($"PolicyEngine:{domain}", value)), Environment(environment), new PolicyEngineValidationOptions());

                var failure = Assert.Single(failures);
                Assert.Contains($"PolicyEngine:{domain}", failure);
                Assert.Contains("must be Off", failure);
                Assert.DoesNotContain(value, failure);
            }

            Assert.Empty(PolicyEngineConfigurationValidator.Evaluate(
                Config(($"PolicyEngine:{domain}", "Off")), Environment(environment), new PolicyEngineValidationOptions()));
        }
    }

    [Fact]
    public void The_check_finds_the_key_in_any_case_and_through_the_environment_variable_form()
    {
        // PolicyEngine__IT.Tickets=Shadow in a compose file reaches the configuration as PolicyEngine:IT.Tickets.
        var fromEnvironment = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["POLICYENGINE:it.tickets"] = "shadow" })
            .Build();

        var failure = Assert.Single(PolicyEngineConfigurationValidator.Evaluate(fromEnvironment, Environment("Production"), new PolicyEngineValidationOptions()));

        Assert.Contains("must be Off", failure);
    }

    [Fact]
    public async Task The_check_throws_at_start_up_with_the_arabic_and_english_message()
    {
        var validator = new PolicyEngineConfigurationValidator(
            Config(($"PolicyEngine:{Retired}", "Shadow")), Environment("Production"),
            Microsoft.Extensions.Options.Options.Create(new PolicyEngineValidationOptions()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.RunAsync(CancellationToken.None));

        Assert.Contains($"PolicyEngine:{Retired} must be Off", ex.Message);
        Assert.Matches("[؀-ۿ]", ex.Message);
    }

    [Fact]
    public void An_out_of_scope_key_does_not_count_as_an_active_domain_and_a_default_fails_nothing()
    {
        var remote = new PolicyEngineValidationOptions { UsesRemoteClient = true };

        // No HR.Core address: the only failure is the scope, not "a policy domain is not Off".
        var onlyScope = PolicyEngineConfigurationValidator.Evaluate(Config(($"PolicyEngine:{Retired}", "Enforce")), Environment("Production"), remote);
        Assert.Single(onlyScope);
        Assert.DoesNotContain(onlyScope, f => f.Contains(HrCoreEndpoint.Key, StringComparison.Ordinal));

        // DefaultMode switches the governed domains on (so a remote client needs the address); it never fails for the others.
        Assert.Empty(PolicyEngineConfigurationValidator.Evaluate(Config(("PolicyEngine:DefaultMode", "Shadow")), Environment("Production"), new PolicyEngineValidationOptions()));
        Assert.Contains(PolicyEngineConfigurationValidator.Evaluate(Config(("PolicyEngine:DefaultMode", "Shadow")), Environment("Production"), remote),
            f => f.Contains(HrCoreEndpoint.Key, StringComparison.Ordinal));
    }

    [Fact]
    public void The_engines_own_settings_are_not_read_as_domains()
    {
        // PLAN-47's Roles, Seeds and Publish keys used to stop the service ("is not a policy domain").
        var configuration = Config(
            ("PolicyEngine:Roles:Author", "rule-writer"),
            ("PolicyEngine:Roles:Approver:0", "rule-checker"),
            ("PolicyEngine:Seeds:RequireApproval", "true"),
            ("PolicyEngine:Publish:AllowRetroactive", "true"),
            ("PolicyEngine:Core.Recruitment", "Shadow"));

        Assert.Empty(PolicyEngineConfigurationValidator.Evaluate(configuration, Environment("Production"), new PolicyEngineValidationOptions()));
        // A key that is neither a domain nor a setting is still refused.
        Assert.Single(PolicyEngineConfigurationValidator.Evaluate(Config(("PolicyEngine:Contracts.Leeve", "Off")), Environment("Production"), new PolicyEngineValidationOptions()));
        Assert.Single(PolicyEngineConfigurationValidator.Evaluate(Config(("PolicyEngine:Rolez:Author", "x")), Environment("Production"), new PolicyEngineValidationOptions()));
    }

    [Fact]
    public async Task Every_host_runs_the_check_whether_or_not_it_registers_the_enforce_client()
    {
        // The shared host set-up registers it, and all fifteen hosts call that set-up.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [$"PolicyEngine:{Retired}"] = "Shadow" });
        builder.AddHrProductionNetworking();
        await using var provider = builder.Services.BuildServiceProvider();

        var check = Assert.Single(provider.GetServices<IHrStartupCheck>().OfType<PolicyEngineConfigurationValidator>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => check.RunAsync(CancellationToken.None));

        var root = Path.Combine(Path.GetDirectoryName(TestSupport.RepoPaths.Resolve("HRManagement.slnx"))!, "src");
        var programs = Directory.EnumerateFiles(root, "Program.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(15, programs.Count);
        Assert.All(programs, p => Assert.Contains("AddHrProductionNetworking()", global::System.IO.File.ReadAllText(p)));
    }

    // ---- the engine ----

    /// <summary>A template of the retired domain as a database seeded before PLAN-49 holds it: a published rule that fires a workflow.</summary>
    private static async Task<(Guid TemplateId, Guid V1Id)> OldTemplateAsync(Ten048PolicyHarness h, string domain = Retired)
    {
        await using var db = h.DesignTime();
        var existing = await db.PolicyTemplates.Include(t => t.Versions)
            .SingleOrDefaultAsync(t => t.Domain == domain && t.Scope == PolicyTemplateScope.National);
        if (existing is not null)
            return (existing.Id, existing.Versions.OrderBy(v => v.VersionNumber).First().Id);

        var template = new PolicyTemplate { TenantId = National, Domain = domain, Name = "قديم", SubjectType = "Ticket", Scope = PolicyTemplateScope.National };
        db.PolicyTemplates.Add(template);
        var v1 = await PolicySeedVersionWriter.AddAsync(db,
            new PolicyTemplateVersion { PolicyTemplate = template, TenantId = National, VersionNumber = 1, EffectiveFrom = DateTime.UtcNow.AddDays(-30) },
            [
                new PolicyRule
                {
                    RuleKey = "R-TKT",
                    Name = "تذكرة",
                    Priority = 10,
                    WhenJson = """{"all":[{"field":"action","operator":"Equals","value":"Open"}]}""",
                    ThenJson = """[{"effect":"TriggerWorkflow","params":{"workflowCode":"IT-TICKET"}}]"""
                }
            ],
            PolicyVersionStatus.Published,
            new PolicyPublication { TenantId = National, PublishedAt = DateTime.UtcNow.AddDays(-30), EvidenceRef = "seed-before-plan-49" });
        return (template.Id, v1.Id);
    }

    private static PolicyEnforceRequest Request(string domain, PolicyEnforcementMode mode) => new()
    {
        Domain = domain,
        TenantId = National,
        SubjectType = "Ticket",
        Action = "Open",
        EntityId = Guid.NewGuid(),
        Mode = mode,
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["action"] = "Open" }
    };

    [Theory]
    [InlineData(PolicyEnforcementMode.Enforce, null)]
    [InlineData(PolicyEnforcementMode.Shadow, null)]
    [InlineData(PolicyEnforcementMode.Enforce, "svc-it-infra")]
    [InlineData(PolicyEnforcementMode.Shadow, "svc-hr-contracts")]
    public async Task The_engine_answers_a_neutral_decision_with_no_ledger_row_and_no_workflow(PolicyEnforcementMode mode, string? caller)
    {
        await using var h = await CreateAsync();
        await OldTemplateAsync(h);
        await using var db = h.Session(National);
        var starter = new Mock<IWorkflowStarter>(MockBehavior.Strict);
        var orchestrator = new PolicyOrchestrator(db, starter.Object, new PolicyPublishedSnapshotCache(new MemoryCache(new MemoryCacheOptions())), NullLogger<PolicyOrchestrator>.Instance);
        var before = await db.PolicyEnforcementLedgers.CountAsync();

        var response = caller is null
            ? await orchestrator.EnforceAsync(Request(Retired, mode))
            : await orchestrator.EnforceForServiceAsync(Request(Retired, mode), caller);
        var evaluated = await orchestrator.EvaluateAsync(Request(Retired, mode));

        Assert.True(response.Allowed);
        Assert.Equal(PolicyEnforcementMode.Off, response.EffectiveMode);
        Assert.Equal([PolicyDomainCatalog.OutOfScopeMessageAr], response.Explanations);
        Assert.Empty(response.Effects);
        Assert.Empty(response.Decision.EffectDetails);
        Assert.Empty(response.Decision.MatchedRuleIds);
        Assert.Null(response.WorkflowInstanceId);
        Assert.Null(response.LedgerId);
        Assert.Null(response.PolicyVersionId);
        Assert.True(evaluated.Allowed);
        Assert.Equal([PolicyDomainCatalog.OutOfScopeMessageAr], evaluated.Explanations);
        Assert.Equal(before, await db.PolicyEnforcementLedgers.CountAsync());
        starter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_name_that_is_no_domain_gets_the_same_neutral_answer()
    {
        await using var h = await CreateAsync();
        await using var db = h.Session(National);
        var orchestrator = new PolicyOrchestrator(db, Mock.Of<IWorkflowStarter>(MockBehavior.Strict), new PolicyPublishedSnapshotCache(new MemoryCache(new MemoryCacheOptions())), NullLogger<PolicyOrchestrator>.Instance);

        var response = await orchestrator.EnforceAsync(Request("Unknown.Domain", PolicyEnforcementMode.Enforce));

        Assert.True(response.Allowed);
        Assert.Equal(PolicyEnforcementMode.Off, response.EffectiveMode);
        Assert.Equal(0, await db.PolicyEnforcementLedgers.CountAsync());
    }

    [Fact]
    public async Task A_governed_domain_is_still_decided_and_recorded()
    {
        await using var h = await CreateAsync();
        await using var db = h.Session(National);
        var request = Request(PolicyDomains.AssetsCustody, PolicyEnforcementMode.Shadow);
        request.SubjectType = "AssetAssignment";
        request.Action = "Assign";
        request.Facts = PrivilegedFacts();

        var response = await Orchestrator(db).EnforceAsync(request);

        Assert.Equal(PolicyEnforcementMode.Shadow, response.EffectiveMode);
        Assert.NotNull(response.LedgerId);
        Assert.Contains("RequireEvidence", response.Effects);
        Assert.Equal(1, await db.PolicyEnforcementLedgers.CountAsync());
    }

    // ---- drafts and publications ----

    private static void AssertOutOfScope(PolicyDraftResult result)
    {
        Assert.Equal(PolicyDraftOutcome.Invalid, result.Outcome);
        var issue = Assert.Single(result.IssueList);
        Assert.Equal(PolicyRuleValidator.OutOfScopeCode, issue.Code);
        Assert.StartsWith(PolicyDomainCatalog.OutOfScopeMessageAr, issue.MessageAr, StringComparison.Ordinal);
    }

    [Theory, MemberData(nameof(OutOfScopeDomains))]
    public async Task No_draft_is_opened_in_an_out_of_scope_domain_by_its_name(string domain)
    {
        await using var h = await CreateAsync();
        await using var db = h.Session(National);
        var templates = await db.PolicyTemplates.CountAsync();
        var versions = await db.PolicyTemplateVersions.CountAsync();

        AssertOutOfScope(await h.Drafts(db).CreateDomainDraftAsync(domain, "Any", "مسودة", null, Author));

        Assert.Equal(templates, await db.PolicyTemplates.CountAsync());
        Assert.Equal(versions, await db.PolicyTemplateVersions.CountAsync());
    }

    [Fact]
    public async Task No_draft_of_any_kind_is_opened_on_an_out_of_scope_template_and_no_rule_is_added_to_one()
    {
        await using var h = await CreateAsync();
        var (templateId, v1) = await OldTemplateAsync(h);
        await using var db = h.Session(National);
        var drafts = h.Drafts(db);

        AssertOutOfScope(await drafts.CreateDraftAsync(templateId, null, Author));
        AssertOutOfScope(await drafts.CreateDraftAsync(templateId, v1, Author));
        AssertOutOfScope(await drafts.CreateRollbackDraftAsync(templateId, v1, "تراجع", Author));
        AssertOutOfScope(await drafts.CreateWithdrawAllDraftAsync(templateId, "سحب", Author));
        Assert.False(await db.PolicyTemplateVersions.AnyAsync(v => v.PolicyTemplateId == templateId && v.Status == PolicyVersionStatus.Draft));

        // A draft left open from before the plan: nothing is added to it and it is never published; it can only be discarded.
        Guid oldDraft;
        await using (var writer = h.DesignTime())
        {
            var template = await writer.PolicyTemplates.SingleAsync(t => t.Id == templateId);
            var draft = await PolicySeedVersionWriter.AddAsync(writer,
                new PolicyTemplateVersion { PolicyTemplate = template, TenantId = National, VersionNumber = 2, CreatedBy = Author }, [], PolicyVersionStatus.Draft, null);
            writer.PolicyVersionEdits.Add(new PolicyVersionEdit { PolicyTemplateVersionId = draft.Id, TenantId = National, UserId = Author, Action = PolicyVersionEditAction.Create });
            await writer.SaveChangesAsync();
            oldDraft = draft.Id;
        }

        var rule = new PolicyRuleInput("R-TKT-NEW", "جديدة", 20, """{"all":[{"field":"action","operator":"Equals","value":"Open"}]}""", """[{"effect":"Deny","params":{"reason":"سبب"}}]""");
        AssertOutOfScope(await drafts.AddRuleAsync(oldDraft, rule, Author));
        var published = await h.Publish(db).PublishAsync(new PolicyPublishCommand(oldDraft, ApproverActor, "EV-49"));
        Assert.Equal(PolicyPublishOutcome.Invalid, published.Outcome);
        Assert.Equal(PolicyRuleValidator.OutOfScopeCode, Assert.Single(published.IssueList).Code);
        Assert.True((await drafts.DiscardDraftAsync(oldDraft, Author)).Succeeded);
    }

    [Fact]
    public async Task The_only_publication_in_an_out_of_scope_domain_is_its_retirement_by_a_seed_change_set()
    {
        await using var h = await CreateAsync();
        var (templateId, v1) = await OldTemplateAsync(h);
        await using var db = h.Session(National);
        var drafts = h.Drafts(db);

        // The draft is the system's (no actor), and only the retirement change set writes it: a person opens no draft of any
        // kind in the domain (the test above), so nobody can prepare a withdrawal of their own.
        var retirement = await drafts.CreateRetirementDraftAsync(templateId, "المجال خارج نطاق المحرك (D-POL-1)");
        Assert.True(retirement.Succeeded, retirement.MessageAr);
        Assert.Null((await db.PolicyTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == retirement.Id)).CreatedBy);
        Assert.Null((await db.PolicyTemplates.AsNoTracking().SingleAsync(t => t.Id == templateId)).RetiredAt);

        var seeded = await h.Publish(db).PublishSystemSeedAsync(retirement.Id!.Value, "POL-TEST-RETIRE", new HashSet<string>());
        Assert.True(seeded.Succeeded, seeded.MessageAr);

        await using var check = h.DesignTime();
        // The publication marks the template, in the same save.
        var retired = await check.PolicyTemplates.AsNoTracking().SingleAsync(t => t.Id == templateId);
        Assert.NotNull(retired.RetiredAt);
        Assert.Equal("المجال خارج نطاق المحرك (D-POL-1)", retired.RetiredReason);
        var versions = await check.PolicyTemplateVersions.Include(v => v.Rules).Include(v => v.Publications)
            .Where(v => v.PolicyTemplateId == templateId).OrderBy(v => v.VersionNumber).ToListAsync();
        var final = versions.Last();
        Assert.Equal(PolicyVersionStatus.Published, final.Status);
        Assert.True(final.WithdrawsAllRules);
        Assert.Empty(final.Rules);
        Assert.Equal(PolicyPublicationKind.SystemSeed, Assert.Single(final.Publications).Kind);
        // The earlier version keeps its rule and is closed, never changed.
        var first = versions.Single(v => v.Id == v1);
        Assert.Single(first.Rules);
        Assert.NotNull(first.EffectiveTo);

        // A system seed that carries a rule into an out-of-scope domain is refused like any other publication.
        await using var writer = h.DesignTime();
        var template = await writer.PolicyTemplates.SingleAsync(t => t.Id == templateId);
        var withRule = await PolicySeedVersionWriter.AddAsync(writer,
            new PolicyTemplateVersion { PolicyTemplate = template, TenantId = National, VersionNumber = final.VersionNumber + 1 },
            [new PolicyRule { RuleKey = "R-BACK", Name = "عودة", Priority = 10, WhenJson = """{"all":[{"field":"action","operator":"Equals","value":"Open"}]}""", ThenJson = """[{"effect":"Deny","params":{"reason":"سبب"}}]""" }],
            PolicyVersionStatus.Draft, null);
        await using var again = h.Session(National);
        var refused = await h.Publish(again).PublishSystemSeedAsync(withRule.Id, "POL-TEST-BACK", new HashSet<string> { "R-BACK" });
        Assert.Equal(PolicyPublishOutcome.Invalid, refused.Outcome);
        Assert.Equal(PolicyRuleValidator.OutOfScopeCode, Assert.Single(refused.IssueList).Code);
    }

    [Fact]
    public async Task A_governed_domain_is_never_retired_by_the_system()
    {
        await using var h = await CreateAsync();
        var custody = await h.NationalTemplateAsync();
        await using var db = h.Session(National);

        var result = await h.Drafts(db).CreateRetirementDraftAsync(custody.Id, "سبب");

        Assert.Equal(PolicyDraftOutcome.Invalid, result.Outcome);
        Assert.False(await db.PolicyTemplateVersions.AnyAsync(v => v.PolicyTemplateId == custody.Id && v.Status == PolicyVersionStatus.Draft));
    }

    // ---- the page and the API ----

    private static PolicyTemplatesController Page(Ten048PolicyHarness h, HRCoreDbContext db)
    {
        var http = new DefaultHttpContext { User = Pol047Harness.User(Author, HrManagementRoles.PolicyAuthor) };
        return new PolicyTemplatesController(db, Mock.Of<IPolicyOrchestrator>(), h.Publish(db), h.Drafts(db), Pol047Harness.Authorization(h.Configuration))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
        };
    }

    [Fact]
    public async Task The_page_refuses_a_draft_of_an_out_of_scope_domain_on_the_form_and_on_the_post()
    {
        await using var h = await CreateAsync();
        await using var db = h.Session(National);
        var controller = Page(h, db);

        var form = Assert.IsType<RedirectToActionResult>(controller.CreateDraft(Retired, "Ticket", Validation(db)));
        Assert.Equal(nameof(PolicyTemplatesController.Index), form.ActionName);
        Assert.StartsWith(PolicyDomainCatalog.OutOfScopeMessageAr, (string)controller.TempData["Error"]!, StringComparison.Ordinal);

        controller.TempData.Clear();
        var post = Assert.IsType<RedirectToActionResult>(await controller.CreateDraft(Retired, "Ticket", "مسودة", null));
        Assert.Equal(nameof(PolicyTemplatesController.Index), post.ActionName);
        Assert.Contains(PolicyDomainCatalog.OutOfScopeMessageAr, (string)controller.TempData["Error"]!);
        Assert.False(await db.PolicyTemplateVersions.AnyAsync(v => v.Status == PolicyVersionStatus.Draft));

        // A governed domain still gets its form.
        Assert.IsType<ViewResult>(controller.CreateDraft(PolicyDomains.AssetsCustody, "Asset", Validation(db)));
    }

    [Fact]
    public async Task The_api_answers_400_with_the_catalog_message_for_a_draft_of_an_out_of_scope_domain()
    {
        await using var h = await CreateAsync();
        var (templateId, _) = await OldTemplateAsync(h);
        await using var db = h.Session(National);
        var controller = new PoliciesController(db, Mock.Of<IPolicyOrchestrator>(), h.Publish(db), h.Drafts(db))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Pol047Harness.User(Author, HrManagementRoles.PolicyAuthor) } }
        };

        var byDomain = Assert.IsType<BadRequestObjectResult>(await controller.CreateDomainDraft(new CreateDomainDraftRequest { Domain = Retired, SubjectType = "Ticket" }, default));
        Assert.Contains(PolicyDomainCatalog.OutOfScopeMessageAr, System.Text.Json.JsonSerializer.Serialize(byDomain.Value, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Assert.Contains(PolicyRuleValidator.OutOfScopeCode, System.Text.Json.JsonSerializer.Serialize(byDomain.Value));

        Assert.IsType<BadRequestObjectResult>(await controller.CreateDraft(templateId, null, default));
        Assert.IsType<BadRequestObjectResult>(await controller.CreateDraft(templateId, new CreatePolicyDraftRequest { WithdrawAllRules = true, Reason = "سحب" }, default));
        Assert.False(await db.PolicyTemplateVersions.AnyAsync(v => v.PolicyTemplateId == templateId && v.Status == PolicyVersionStatus.Draft));
    }

    [Fact]
    public async Task The_page_lists_twenty_domains_from_the_catalog_with_the_mode_the_resolver_gives()
    {
        await using var h = await CreateAsync();
        await using var db = h.Session(National);
        // One ledger row of a governed domain, so the page shows a latest decision.
        var request = Request(PolicyDomains.AssetsCustody, PolicyEnforcementMode.Shadow);
        request.SubjectType = "AssetAssignment";
        request.Action = "Assign";
        request.Facts = PrivilegedFacts();
        await Orchestrator(db).EnforceAsync(request);
        // Shadow for the governed domains, as a production file has it; a stray default must not reach the others.
        var resolver = new PolicyModeResolver(Config(("PolicyEngine:DefaultMode", "Shadow")));

        var view = Assert.IsType<ViewResult>(await Page(h, db).Index(resolver));

        var rows = Assert.IsAssignableFrom<IReadOnlyList<PolicyDomainRow>>(view.ViewData["Domains"]);
        Assert.Equal(PolicyDomainCatalog.All.Select(d => d.Domain), rows.Select(r => r.Descriptor.Domain));
        Assert.Equal(5, rows.Count(r => r.Descriptor.IsGoverned));
        Assert.All(rows.Where(r => r.Descriptor.IsGoverned), r => Assert.Equal(PolicyEnforcementMode.Shadow, r.Mode));
        Assert.Equal(15, rows.Count(r => !r.Descriptor.IsGoverned));
        Assert.All(rows.Where(r => !r.Descriptor.IsGoverned), r =>
        {
            Assert.Equal(PolicyEnforcementMode.Off, r.Mode);
            Assert.Null(r.LastDecision);
            Assert.False(string.IsNullOrWhiteSpace(r.Descriptor.OutOfScopeReasonAr));
        });
        var custody = rows.Single(r => r.Descriptor.Domain == PolicyDomains.AssetsCustody);
        Assert.Equal(("Assign", "Shadow"), (custody.LastDecision!.Action, custody.LastDecision.Mode));
    }
}
