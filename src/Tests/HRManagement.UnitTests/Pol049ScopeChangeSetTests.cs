using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-49 (plan section 2, P-4): what the four change sets do to a database from before the plan, on a context with the
/// immutability interceptor. The fifteen out-of-scope templates get one final version with no rules and are marked retired; the
/// custody rules gain their action; the orphan card rule and the placeholder leave rules go. Every change is a new published
/// version: the old versions keep every rule they had. A later start changes nothing, an open draft postpones, and a setting
/// that asks for an approver is honoured.
/// </summary>
public sealed class Pol049ScopeChangeSetTests
{
    private static readonly string[] RetiredDomains = [.. PolicyDomainCatalog.OutOfScopeDomains.Select(d => d.Domain)];

    /// <summary>The database of before the plan (the demo server's shape), with the workflow definitions it points at.</summary>
    private static async Task<Pol047Harness> LegacyAsync(Dictionary<string, string?>? settings = null)
    {
        var h = new Pol047Harness(settings);
        await WorkflowPackSeeder.SeedAsync(h.Db);
        await WorkflowPackSeeder.EnsureCustodyChainAsync(h.Db);
        await WorkflowPackSeeder.EnsureCrossServiceChainsAsync(h.Db);
        await Pol049LegacyPolicyData.SeedAsync(h.Db);
        return h;
    }

    private static async Task BootAsync(Pol047Harness h, IServiceProvider? services = null)
    {
        await WorkflowPackSeeder.SeedAsync(h.Db);
        await PolicyEngineSeeder.SeedAsync(h.Db, services);
        h.Db.ChangeTracker.Clear();
    }

    private static Task<List<PolicyTemplateVersion>> VersionsAsync(Pol047Harness h, string domain) =>
        h.Db.PolicyTemplateVersions.AsNoTracking().Include(v => v.Rules).Include(v => v.Publications).Include(v => v.PolicyTemplate)
            .Where(v => v.PolicyTemplate.Domain == domain && v.PolicyTemplate.Scope == PolicyTemplateScope.National)
            .OrderBy(v => v.VersionNumber).ToListAsync();

    /// <summary>Every rule of every published version, as stored: what "never an edit in place" compares.</summary>
    private static async Task<Dictionary<Guid, string>> RuleFingerprintAsync(Pol047Harness h) =>
        await h.Db.PolicyRules.AsNoTracking()
            .ToDictionaryAsync(r => r.Id, r => $"{r.PolicyTemplateVersionId}|{r.RuleKey}|{r.Name}|{r.Priority}|{r.WhenJson}|{r.ThenJson}");

    private static IServiceProvider Services(IConfiguration configuration, ILoggerProvider? logs = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Debug);
            if (logs is not null)
                b.AddProvider(logs);
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public void The_retirement_set_names_every_out_of_scope_domain_once_with_a_reason_and_no_governed_one()
    {
        var operations = PolicySeedChangeSets.ScopeRetire.Operations;

        Assert.All(operations, o => Assert.Equal(PolicySeedOperationKind.RetireDomain, o.Kind));
        Assert.Equal(RetiredDomains.OrderBy(d => d, StringComparer.Ordinal), operations.Select(o => o.Domain).OrderBy(d => d, StringComparer.Ordinal));
        Assert.All(operations, o =>
        {
            Assert.False(string.IsNullOrWhiteSpace(o.ReasonAr), o.Domain);
            Assert.Matches("[؀-ۿ]", o.ReasonAr!);
            Assert.True(o.ReasonAr!.Length <= PolicyDraftService.MaxReasonLength);
            Assert.Null(o.RuleKey);
        });

        // The four sets of this plan, after the two of PLAN-47, in the order they apply; nothing before them moved.
        Assert.Equal(
            [
                PolicySeedChangeSets.BaselineId, PolicySeedChangeSets.RetireRecruitmentHireId, PolicySeedChangeSets.ScopeRetireId,
                PolicySeedChangeSets.AssetsActionId, PolicySeedChangeSets.IdCardOrphanId, PolicySeedChangeSets.LeaveId
            ],
            PolicySeedChangeSets.All.Select(s => s.Id));
        Assert.Equal(PolicySeedChangeSets.All.Count, PolicySeedChangeSets.All.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// An applied set never changes: the demo server recorded these digests on 2026-11-08 (PLAN-47), and a start-up whose code
    /// computes another one stops. The first sixteen characters of each, read from the server before this plan's deploy.
    /// </summary>
    [Theory]
    [InlineData(PolicySeedChangeSets.BaselineId, PolicyDomains.ContractsLeave, "0758c541748d9346")]
    [InlineData(PolicySeedChangeSets.BaselineId, PolicyDomains.CoreRecruitment, "7b4cf94d51606730")]
    [InlineData(PolicySeedChangeSets.BaselineId, PolicyDomains.ArchiveRetention, "6bb75e52dbe0c8c2")]
    [InlineData(PolicySeedChangeSets.BaselineId, PolicyDomains.AssetsCustody, "dc22b7427d4197b6")]
    [InlineData(PolicySeedChangeSets.RetireRecruitmentHireId, PolicyDomains.CoreRecruitment, "c51916da8a070a35")]
    public void The_sets_the_demo_server_applied_keep_their_digests(string setId, string domain, string serverDigestStart)
    {
        var set = PolicySeedChangeSets.All.Single(s => s.Id == setId);

        Assert.StartsWith(serverDigestStart, set.Digest(domain), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_existing_database_gets_a_final_version_for_each_out_of_scope_template_and_keeps_its_history()
    {
        await using var h = await LegacyAsync();
        var rulesBefore = await RuleFingerprintAsync(h);
        var versionsBefore = await h.Db.PolicyTemplateVersions.AsNoTracking().ToDictionaryAsync(v => v.Id, v => v.VersionNumber);
        Assert.Equal(20, await h.Db.PolicyTemplates.CountAsync());

        await BootAsync(h);

        foreach (var domain in RetiredDomains)
        {
            var reason = PolicySeedChangeSets.ScopeRetire.For(domain).Single().ReasonAr;
            var template = await h.Db.PolicyTemplates.AsNoTracking().SingleAsync(t => t.Domain == domain);
            Assert.NotNull(template.RetiredAt);
            Assert.Equal(reason, template.RetiredReason);

            var versions = await VersionsAsync(h, domain);
            Assert.Equal([1, 2], versions.Select(v => v.VersionNumber));
            var final = versions[1];
            Assert.Equal(PolicyVersionStatus.Published, final.Status);
            Assert.True(final.WithdrawsAllRules);
            Assert.Empty(final.Rules);
            Assert.Equal(reason, final.ChangeReason);
            Assert.Null(final.EffectiveTo);
            var publication = Assert.Single(final.Publications);
            Assert.Equal(PolicyPublicationKind.SystemSeed, publication.Kind);
            Assert.Equal(PolicySeedChangeSets.ScopeRetireId, publication.EvidenceRef);
            Assert.Null(publication.MakerUserId);

            // The version before it is closed by the publication and is otherwise what it was.
            Assert.Equal(PolicyVersionStatus.Published, versions[0].Status);
            Assert.NotNull(versions[0].EffectiveTo);
            Assert.Same(final, PolicyDraftService.EffectiveVersion(TemplateOf(versions), DateTime.UtcNow));

            var application = await h.Db.PolicySeedApplications.AsNoTracking()
                .SingleAsync(a => a.ChangeSetId == PolicySeedChangeSets.ScopeRetireId && a.PolicyTemplateId == template.Id);
            Assert.Equal(PolicySeedChangeSetApplier.OutcomePublished, application.Outcome);
            Assert.Equal(final.Id, application.ResultVersionId);
        }

        // Never an edit in place: every rule and every version that was there is there, unchanged.
        var rulesAfter = await RuleFingerprintAsync(h);
        Assert.All(rulesBefore, kv => Assert.Equal(kv.Value, rulesAfter[kv.Key]));
        var versionsAfter = await h.Db.PolicyTemplateVersions.AsNoTracking().ToDictionaryAsync(v => v.Id, v => v.VersionNumber);
        Assert.All(versionsBefore, kv => Assert.Equal(kv.Value, versionsAfter[kv.Key]));

        // The five governed templates are not retired.
        var governed = await h.Db.PolicyTemplates.AsNoTracking().Where(t => t.RetiredAt == null).Select(t => t.Domain).ToListAsync();
        Assert.Equal(PolicyDomainCatalog.Governed.Select(d => d.Domain).OrderBy(d => d, StringComparer.Ordinal), governed.OrderBy(d => d, StringComparer.Ordinal));
    }

    private static PolicyTemplate TemplateOf(List<PolicyTemplateVersion> versions)
    {
        var template = versions[0].PolicyTemplate;
        template.Versions = versions;
        return template;
    }

    [Fact]
    public async Task The_custody_rules_gain_their_action_the_card_rule_and_the_placeholder_leave_rules_go()
    {
        await using var h = await LegacyAsync();

        await BootAsync(h);

        // Assets.Custody: v2 by POL-049-ASSETS-ACTION; the three rules are bound to Assign, the three others are carried over.
        var custody = await VersionsAsync(h, PolicyDomains.AssetsCustody);
        Assert.Equal([1, 2], custody.Select(v => v.VersionNumber));
        Assert.Equal(6, custody[0].Rules.Count);
        Assert.Equal(6, custody[1].Rules.Count);
        foreach (var rule in PolicySeedChangeSets.CustodyAssignRules)
        {
            var old = custody[0].Rules.Single(r => r.RuleKey == rule.Key);
            Assert.DoesNotContain("\"action\"", old.WhenJson, StringComparison.Ordinal);
            var now = custody[1].Rules.Single(r => r.RuleKey == rule.Key);
            Assert.Equal(rule.When, now.WhenJson);
            Assert.Contains("""{"field":"action","operator":"Equals","value":"Assign"}""", now.WhenJson, StringComparison.Ordinal);
            Assert.Equal(old.ThenJson, now.ThenJson);
            Assert.Equal(old.Priority, now.Priority);
            Assert.Equal(old.Name, now.Name);
        }
        foreach (var kept in new[] { "R-CLASSIFY-HIGH-VALUE", "R-CLASSIFY-CAPITAL", "R-PRIV-CODE" })
            Assert.Equal(custody[0].Rules.Single(r => r.RuleKey == kept).WhenJson, custody[1].Rules.Single(r => r.RuleKey == kept).WhenJson);
        Assert.Equal(PolicySeedChangeSets.AssetsActionId, Assert.Single(custody[1].Publications).EvidenceRef);

        // IDCard.Lifecycle: v2 by POL-049-IDCARD-ORPHAN, with no rule left: an explicit withdrawal with the set's reason.
        var card = await VersionsAsync(h, PolicyDomains.IDCardLifecycle);
        Assert.Equal([1, 2], card.Select(v => v.VersionNumber));
        Assert.Equal("R-ID-PROCESS", Assert.Single(card[0].Rules).RuleKey);
        Assert.Empty(card[1].Rules);
        Assert.True(card[1].WithdrawsAllRules);
        Assert.Contains("PLAN-46", card[1].ChangeReason, StringComparison.Ordinal);
        Assert.Equal(PolicySeedChangeSets.IdCardOrphanId, Assert.Single(card[1].Publications).EvidenceRef);
        // The domain is governed: its template is not retired, and it can take rules again.
        Assert.Null((await h.Db.PolicyTemplates.AsNoTracking().SingleAsync(t => t.Domain == PolicyDomains.IDCardLifecycle)).RetiredAt);

        // Contracts.Leave: v2 by POL-049-LEAVE; only the self-approval rule stays, bound to Approve.
        var leave = await VersionsAsync(h, PolicyDomains.ContractsLeave);
        Assert.Equal([1, 2], leave.Select(v => v.VersionNumber));
        Assert.Equal(["R-LEAVE-LONG", "R-LEAVE-MAX", "R-LEAVE-SELF"], leave[0].Rules.Select(r => r.RuleKey).OrderBy(k => k, StringComparer.Ordinal));
        var self = Assert.Single(leave[1].Rules);
        Assert.Equal("R-LEAVE-SELF", self.RuleKey);
        Assert.Equal(PolicySeedChangeSets.LeaveSelfApprovalRule.When, self.WhenJson);
        Assert.Contains("""{"field":"action","operator":"Equals","value":"Approve"}""", self.WhenJson, StringComparison.Ordinal);
        Assert.Equal(PolicySeedChangeSets.LeaveId, Assert.Single(leave[1].Publications).EvidenceRef);

        // Core.Recruitment and Core.Establishment: nothing to change (the server's applied sets stay recorded as they were).
        Assert.Single(await VersionsAsync(h, PolicyDomains.CoreRecruitment));
        Assert.Single(await VersionsAsync(h, PolicyDomains.CoreEstablishment));

        // The establishment code gets the binding it lacked, unpinned; the thirteen old bindings stay as they were.
        var bindings = await h.Db.PolicyWorkflowBindings.AsNoTracking().ToListAsync();
        Assert.Equal(Pol049LegacyPolicyData.Bindings.Count + 1, bindings.Count);
        var establishment = bindings.Single(b => b.WorkflowCode == "ESTABLISHMENT-CHANGE-APPROVAL");
        Assert.Equal(PolicyDomains.CoreEstablishment, establishment.Domain);
        Assert.Null(establishment.WorkflowDefinitionId);
        Assert.All(Pol049LegacyPolicyData.Bindings, old => Assert.Contains(bindings, b => b.Domain == old.Domain && b.WorkflowCode == old.WorkflowCode && (b.WorkflowDefinitionId is not null) == old.Pinned));
    }

    [Fact]
    public async Task A_second_and_a_third_start_change_nothing()
    {
        await using var h = await LegacyAsync();
        await BootAsync(h);
        var counts = await CountsAsync(h);
        var retiredAt = await h.Db.PolicyTemplates.AsNoTracking().Where(t => t.RetiredAt != null).ToDictionaryAsync(t => t.Id, t => t.RetiredAt);

        await BootAsync(h);
        await BootAsync(h);

        Assert.Equal(counts, await CountsAsync(h));
        var again = await h.Db.PolicyTemplates.AsNoTracking().Where(t => t.RetiredAt != null).ToDictionaryAsync(t => t.Id, t => t.RetiredAt);
        Assert.Equal(15, again.Count);
        Assert.All(retiredAt, kv => Assert.Equal(kv.Value, again[kv.Key]));
        // One application of each new set per template it touches.
        var applications = await h.Db.PolicySeedApplications.AsNoTracking().ToListAsync();
        Assert.Equal(15, applications.Count(a => a.ChangeSetId == PolicySeedChangeSets.ScopeRetireId));
        Assert.Single(applications, a => a.ChangeSetId == PolicySeedChangeSets.AssetsActionId);
        Assert.Single(applications, a => a.ChangeSetId == PolicySeedChangeSets.IdCardOrphanId);
        Assert.Single(applications, a => a.ChangeSetId == PolicySeedChangeSets.LeaveId);
    }

    private static async Task<(int Templates, int Versions, int Rules, int Publications, int Applications, int Bindings, int Edits)> CountsAsync(Pol047Harness h) =>
        (await h.Db.PolicyTemplates.CountAsync(), await h.Db.PolicyTemplateVersions.CountAsync(), await h.Db.PolicyRules.CountAsync(),
            await h.Db.PolicyPublications.CountAsync(), await h.Db.PolicySeedApplications.CountAsync(), await h.Db.PolicyWorkflowBindings.CountAsync(),
            await h.Db.PolicyVersionEdits.CountAsync());

    [Fact]
    public async Task A_new_database_has_nothing_to_retire_and_the_sets_pass_over_the_fifteen_without_a_warning()
    {
        await using var h = new Pol047Harness();
        var logs = new ListLoggerProvider();
        var services = Services(h.Configuration, logs);

        for (var boot = 0; boot < 2; boot++)
            await BootAsync(h, services);

        var domains = await h.Db.PolicyTemplates.AsNoTracking().Select(t => t.Domain).ToListAsync();
        Assert.Equal(PolicyDomainCatalog.Governed.Select(d => d.Domain).OrderBy(d => d, StringComparer.Ordinal), domains.OrderBy(d => d, StringComparer.Ordinal));
        Assert.False(await h.Db.PolicyTemplates.AnyAsync(t => t.RetiredAt != null));
        Assert.False(await h.Db.PolicySeedApplications.AnyAsync(a => a.ChangeSetId == PolicySeedChangeSets.ScopeRetireId));
        // "No template" is worth a warning only for a governed domain: for the fifteen the catalog already says why.
        Assert.DoesNotContain(logs.Entries, e => e.Level >= LogLevel.Warning);
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("out of the engine's scope", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_open_draft_postpones_the_retirement_until_it_is_discarded()
    {
        await using var h = await LegacyAsync();
        // A draft somebody opened before the plan (the draft service opens none in such a domain now).
        var template = await h.Db.PolicyTemplates.SingleAsync(t => t.Domain == PolicyDomains.ITTickets);
        var author = Guid.NewGuid();
        var draft = await PolicySeedVersionWriter.AddAsync(h.Db,
            new PolicyTemplateVersion { PolicyTemplate = template, TenantId = template.TenantId, VersionNumber = 2, CreatedBy = author }, [], PolicyVersionStatus.Draft, null);
        h.Db.PolicyVersionEdits.Add(new PolicyVersionEdit { PolicyTemplateVersionId = draft.Id, TenantId = template.TenantId, UserId = author, Action = PolicyVersionEditAction.Create });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        await BootAsync(h);

        Assert.Null((await h.Db.PolicyTemplates.AsNoTracking().SingleAsync(t => t.Id == template.Id)).RetiredAt);
        Assert.False(await h.Db.PolicySeedApplications.AnyAsync(a => a.ChangeSetId == PolicySeedChangeSets.ScopeRetireId && a.PolicyTemplateId == template.Id));
        Assert.Equal(14, await h.Db.PolicyTemplates.CountAsync(t => t.RetiredAt != null));
        // Until then the engine still answers nothing for the domain: the catalog decides at run time, not the data.
        Assert.False(PolicyDomainCatalog.IsGoverned(PolicyDomains.ITTickets));

        Assert.True((await h.Drafts.DiscardDraftAsync(draft.Id, author)).Succeeded);
        h.Db.ChangeTracker.Clear();
        await BootAsync(h);

        Assert.NotNull((await h.Db.PolicyTemplates.AsNoTracking().SingleAsync(t => t.Id == template.Id)).RetiredAt);
        var final = (await VersionsAsync(h, PolicyDomains.ITTickets)).Last();
        Assert.True(final.WithdrawsAllRules);
        Assert.Equal(PolicyVersionStatus.Published, final.Status);
    }

    [Fact]
    public async Task With_seeds_requiring_approval_the_retirement_waits_for_an_approver_who_can_only_publish_it_as_it_is()
    {
        await using var h = await LegacyAsync(new() { [PolicySeedChangeSetApplier.RequireApprovalKey] = "true" });
        var services = Services(h.Configuration);

        await PolicySeedChangeSetApplier.Create(h.Db, services).ApplyAsync([PolicySeedChangeSets.ScopeRetire]);
        h.Db.ChangeTracker.Clear();

        Assert.False(await h.Db.PolicyTemplates.AnyAsync(t => t.RetiredAt != null));
        var applications = await h.Db.PolicySeedApplications.AsNoTracking().Where(a => a.ChangeSetId == PolicySeedChangeSets.ScopeRetireId).ToListAsync();
        Assert.Equal(15, applications.Count);
        Assert.All(applications, a => Assert.Equal(PolicySeedChangeSetApplier.OutcomeDraft, a.Outcome));

        var template = await h.Db.PolicyTemplates.AsNoTracking().SingleAsync(t => t.Domain == PolicyDomains.PayrollPeriod);
        var draftId = applications.Single(a => a.PolicyTemplateId == template.Id).ResultVersionId!.Value;
        var draft = await h.Db.PolicyTemplateVersions.AsNoTracking().Include(v => v.Rules).SingleAsync(v => v.Id == draftId);
        Assert.Equal(PolicyVersionStatus.Draft, draft.Status);
        Assert.True(draft.WithdrawsAllRules);
        Assert.Empty(draft.Rules);
        Assert.Null(draft.CreatedBy);

        // Nobody adds a rule to it: the domain is out of scope.
        var added = await h.Drafts.AddRuleAsync(draftId, new("R-PAY-NEW", "جديدة", 20, """{"all":[{"field":"action","operator":"Equals","value":"Close"}]}""", """[{"effect":"Deny","params":{"reason":"سبب"}}]"""), Guid.NewGuid());
        Assert.False(added.Succeeded);
        h.Db.ChangeTracker.Clear();

        var approver = Guid.NewGuid();
        var published = await h.PublishService.PublishAsync(new(draftId, Pol047Harness.Actor(approver, HrManagementRoles.PolicyApprover), "EV-49"));
        Assert.True(published.Succeeded, published.MessageAr);
        h.Db.ChangeTracker.Clear();

        var retired = await h.Db.PolicyTemplates.AsNoTracking().SingleAsync(t => t.Id == template.Id);
        Assert.NotNull(retired.RetiredAt);
        Assert.Equal(draft.ChangeReason, retired.RetiredReason);
        var publication = await h.Db.PolicyPublications.AsNoTracking().SingleAsync(p => p.PolicyTemplateVersionId == draftId);
        Assert.Equal(PolicyPublicationKind.Manual, publication.Kind);
        Assert.Equal(approver, publication.CheckerUserId);

        // A later start neither repeats the set nor touches the fourteen drafts that still wait.
        var versions = await h.Db.PolicyTemplateVersions.CountAsync();
        await PolicySeedChangeSetApplier.Create(h.Db, services).ApplyAsync([PolicySeedChangeSets.ScopeRetire]);
        Assert.Equal(versions, await h.Db.PolicyTemplateVersions.CountAsync());
        Assert.Equal(15, await h.Db.PolicySeedApplications.CountAsync(a => a.ChangeSetId == PolicySeedChangeSets.ScopeRetireId));
    }

    [Fact]
    public async Task A_governed_domain_named_by_a_retirement_operation_is_left_alone()
    {
        // A domain that came back into scope after a retirement set was written: the set no longer acts on it.
        await using var h = await LegacyAsync();
        var set = new PolicySeedChangeSet("POL-TEST-RETIRE-GOVERNED",
        [
            new(PolicySeedOperationKind.RetireDomain, PolicyDomains.CoreEstablishment, ReasonAr: "سبب")
        ]);

        await PolicySeedChangeSetApplier.Create(h.Db, null).ApplyAsync([set]);
        h.Db.ChangeTracker.Clear();

        var template = await h.Db.PolicyTemplates.AsNoTracking().SingleAsync(t => t.Domain == PolicyDomains.CoreEstablishment);
        Assert.Null(template.RetiredAt);
        Assert.Single(await VersionsAsync(h, PolicyDomains.CoreEstablishment));
        Assert.Equal(PolicySeedChangeSetApplier.OutcomeNoOp, (await h.Db.PolicySeedApplications.AsNoTracking().SingleAsync(a => a.ChangeSetId == "POL-TEST-RETIRE-GOVERNED")).Outcome);
    }

    [Fact]
    public async Task A_template_that_publishes_rules_again_is_no_longer_marked_retired()
    {
        // Bringing a domain back is a descriptor, a call site and a change set; the mark goes with the first rules it publishes.
        await using var h = await LegacyAsync();
        await BootAsync(h);
        var template = await h.Db.PolicyTemplates.SingleAsync(t => t.Domain == PolicyDomains.CoreEstablishment);
        template.RetiredAt = DateTime.UtcNow.AddDays(-1);
        template.RetiredReason = "كان خارج النطاق";
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var set = new PolicySeedChangeSet("POL-TEST-BACK-IN-SCOPE",
        [
            new(PolicySeedOperationKind.AddRule, PolicyDomains.CoreEstablishment, "R-EST-NOTE", "ملاحظة", 20,
                """{"all":[{"field":"action","operator":"Equals","value":"SubmitForApproval"},{"field":"changeType","operator":"Equals","value":"X"}]}""",
                """[{"effect":"Allow"}]""")
        ]);

        await PolicySeedChangeSetApplier.Create(h.Db, null).ApplyAsync([set]);
        h.Db.ChangeTracker.Clear();

        var after = await h.Db.PolicyTemplates.AsNoTracking().SingleAsync(t => t.Id == template.Id);
        Assert.Null(after.RetiredAt);
        Assert.Null(after.RetiredReason);
    }

    private sealed class ListLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Category, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new ListLogger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class ListLogger(ListLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner.Entries)
                    owner.Entries.Add((logLevel, category, formatter(state, exception)));
            }
        }
    }
}
