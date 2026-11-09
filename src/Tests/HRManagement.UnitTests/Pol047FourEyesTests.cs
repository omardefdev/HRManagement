using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Services.Policy;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-47 §2 (D-POL-3): nobody publishes what they wrote or changed. The publisher is an identified approver who is not among the
/// draft's editors; there is no override outside Development; the roles come from configuration.
/// </summary>
public sealed class Pol047FourEyesTests
{
    private static readonly Guid Author = Guid.NewGuid();
    private static readonly Guid Approver = Guid.NewGuid();

    [Fact]
    public async Task An_approver_who_did_not_edit_publishes_and_the_publication_names_both()
    {
        await using var h = new Pol047Harness();
        var (template, v1) = await h.SeedTemplateAsync();
        var draftId = await h.DraftByAsync(template.Id, Author);

        var result = await h.PublishService.PublishAsync(new(draftId, Pol047Harness.Actor(Approver, HrManagementRoles.HrManagementApprover), "EV-1"));

        Assert.True(result.Succeeded, result.MessageAr);
        var publication = await h.Db.PolicyPublications.SingleAsync(p => p.PolicyTemplateVersionId == draftId);
        Assert.Equal(Author, publication.MakerUserId);
        Assert.Equal(Approver, publication.CheckerUserId);
        Assert.Equal(PolicyPublicationKind.Manual, publication.Kind);
        Assert.Equal("policy-author", publication.MakerRole);
        Assert.Equal(HrManagementRoles.HrManagementApprover, publication.CheckerRole);   // the role the checker actually holds
        Assert.Equal("EV-1", publication.EvidenceRef);
        h.Guard.Verify(g => g.ValidateFourEyesPublish("policy-author", HrManagementRoles.HrManagementApprover), Times.Once);
        h.Guard.Verify(g => g.ValidateInvalidPublish(true, true), Times.Once);
        Assert.NotNull((await h.Db.PolicyTemplateVersions.SingleAsync(v => v.Id == v1.Id)).EffectiveTo);
    }

    [Fact]
    public async Task The_author_cannot_publish_their_own_draft_even_holding_both_roles()
    {
        await using var h = new Pol047Harness();
        var (template, _) = await h.SeedTemplateAsync();
        var both = Guid.NewGuid();
        var draftId = await h.DraftByAsync(template.Id, both);

        var result = await h.PublishService.PublishAsync(new(draftId, Pol047Harness.Actor(both, HrManagementRoles.HrCore, HrManagementRoles.HrManagementApprover), null));

        Assert.Equal(PolicyPublishOutcome.FourEyes, result.Outcome);
        Assert.Equal(PolicyVersionStatus.Draft, (await h.Db.PolicyTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == draftId)).Status);
        Assert.False(await h.Db.PolicyPublications.AnyAsync(p => p.PolicyTemplateVersionId == draftId));
    }

    [Fact]
    public async Task An_approver_who_changed_one_rule_is_an_editor()
    {
        await using var h = new Pol047Harness();
        var (template, _) = await h.SeedTemplateAsync();
        var draftId = await h.DraftByAsync(template.Id, Author);
        var rule = await h.Db.PolicyRules.AsNoTracking().FirstAsync(r => r.PolicyTemplateVersionId == draftId && r.RuleKey == "R-NEW");
        var changed = await h.Drafts.UpdateRuleAsync(draftId, rule.Id, new PolicyRuleInput("R-NEW", "changed", 21, Pol047Harness.ValidWhen, Pol047Harness.ValidThen), Approver);
        Assert.True(changed.Succeeded, changed.MessageAr);

        var result = await h.PublishService.PublishAsync(new(draftId, Pol047Harness.Actor(Approver, HrManagementRoles.HrManagementApprover), null));

        Assert.Equal(PolicyPublishOutcome.FourEyes, result.Outcome);
        var actions = await h.Db.PolicyVersionEdits.Where(e => e.PolicyTemplateVersionId == draftId).Select(e => e.Action).ToListAsync();
        Assert.Equal([PolicyVersionEditAction.Copy, PolicyVersionEditAction.AddRule, PolicyVersionEditAction.UpdateRule], actions);
    }

    [Theory]
    [InlineData(HrManagementRoles.HrCore)]
    [InlineData(HrManagementRoles.PolicyAuthor)]
    [InlineData(HrManagementRoles.HrCoreSupervisory)]
    public async Task A_publisher_without_the_approver_policy_is_refused(string role)
    {
        await using var h = new Pol047Harness();
        var (template, _) = await h.SeedTemplateAsync();
        var draftId = await h.DraftByAsync(template.Id, Author);

        var result = await h.PublishService.PublishAsync(new(draftId, Pol047Harness.Actor(Approver, role), null));

        Assert.Equal(PolicyPublishOutcome.FourEyes, result.Outcome);
    }

    [Fact]
    public async Task An_unidentified_publisher_or_a_draft_with_no_editor_is_refused()
    {
        await using var h = new Pol047Harness();
        var (template, _) = await h.SeedTemplateAsync();
        var draftId = await h.DraftByAsync(template.Id, Author);

        var anonymous = await h.PublishService.PublishAsync(new(draftId, new PolicyActor(null, Pol047Harness.User(Approver, HrManagementRoles.HrManagementApprover)), null));
        Assert.Equal(PolicyPublishOutcome.FourEyes, anonymous.Outcome);

        // A draft written outside the draft service (SQL, an old seed) with no CreatedBy: four eyes cannot be proved.
        var orphan = new PolicyTemplateVersion { PolicyTemplateId = template.Id, TenantId = Pol047Harness.Tenant, VersionNumber = 9, Status = PolicyVersionStatus.Draft };
        await h.Drafts.DiscardDraftAsync(draftId, Author);
        h.Db.PolicyTemplateVersions.Add(orphan);
        h.Db.PolicyRules.Add(new PolicyRule { PolicyTemplateVersionId = orphan.Id, TenantId = Pol047Harness.Tenant, RuleKey = "R-O", Priority = 1, WhenJson = Pol047Harness.ValidWhen, ThenJson = Pol047Harness.ValidThen });
        await h.Db.SaveChangesAsync();

        var noEditor = await h.PublishService.PublishAsync(new(orphan.Id, Pol047Harness.Actor(Approver, HrManagementRoles.HrManagementApprover), null));
        Assert.Equal(PolicyPublishOutcome.Invalid, noEditor.Outcome);
        Assert.Contains("four-eyes", noEditor.MessageAr);
    }

    [Theory]
    [InlineData("Development", "true", true)]
    [InlineData("Development", "false", false)]
    [InlineData("Production", "true", false)]
    [InlineData("Acceptance", "true", false)]
    public async Task The_only_same_actor_exception_is_the_development_setting(string environment, string setting, bool published)
    {
        await using var h = new Pol047Harness(new() { [PolicyPublishService.AllowSameActorInDevelopmentKey] = setting }, environment);
        var (template, _) = await h.SeedTemplateAsync();
        var both = Guid.NewGuid();
        var draftId = await h.DraftByAsync(template.Id, both);

        var result = await h.PublishService.PublishAsync(new(draftId, Pol047Harness.Actor(both, HrManagementRoles.HrManagementApprover), null));

        Assert.Equal(published, result.Succeeded);
    }

    [Fact]
    public async Task The_roles_follow_the_configuration()
    {
        var configured = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PolicyEngineRoleHandler.AuthorRolesKey] = "rule-writer",
                ["PolicyEngine:Roles:Approver:0"] = "rule-checker",
                ["PolicyEngine:Roles:Approver:1"] = HrManagementRoles.Admin
            }).Build();
        var auth = Pol047Harness.Authorization(configured);
        var defaults = Pol047Harness.Authorization(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var id = Guid.NewGuid();

        Assert.True((await auth.AuthorizeAsync(Pol047Harness.User(id, "rule-writer"), HrManagementPolicies.PolicyAuthor)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(Pol047Harness.User(id, HrManagementRoles.HrCore), HrManagementPolicies.PolicyAuthor)).Succeeded);
        Assert.True((await auth.AuthorizeAsync(Pol047Harness.User(id, "rule-checker"), HrManagementPolicies.PolicyApprover)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(Pol047Harness.User(id, HrManagementRoles.HrManagementApprover), HrManagementPolicies.PolicyApprover)).Succeeded);

        foreach (var role in new[] { HrManagementRoles.Admin, HrManagementRoles.HrCore, HrManagementRoles.PolicyAuthor })
            Assert.True((await defaults.AuthorizeAsync(Pol047Harness.User(id, role), HrManagementPolicies.PolicyAuthor)).Succeeded, role);
        foreach (var role in new[] { HrManagementRoles.Admin, HrManagementRoles.HrManagementApprover, HrManagementRoles.PolicyApprover })
            Assert.True((await defaults.AuthorizeAsync(Pol047Harness.User(id, role), HrManagementPolicies.PolicyApprover)).Succeeded, role);
        Assert.False((await defaults.AuthorizeAsync(Pol047Harness.User(id, HrManagementRoles.HrCore), HrManagementPolicies.PolicyApprover)).Succeeded);
        Assert.False((await defaults.AuthorizeAsync(Pol047Harness.User(id, HrManagementRoles.HrManagementApprover), HrManagementPolicies.PolicyAuthor)).Succeeded);
        Assert.True((await defaults.AuthorizeAsync(Pol047Harness.User(id, HrManagementRoles.PolicyApprover), HrManagementPolicies.PolicyReader)).Succeeded);
        Assert.False((await defaults.AuthorizeAsync(Pol047Harness.User(id, HrManagementRoles.HrEmployee), HrManagementPolicies.PolicyReader)).Succeeded);

        // An author or an approver is a person the records can name: a token with no user id holds neither policy.
        var nameless = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, HrManagementRoles.HrCore),
             new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, HrManagementRoles.HrManagementApprover)], "test"));
        Assert.False((await defaults.AuthorizeAsync(nameless, HrManagementPolicies.PolicyAuthor)).Succeeded);
        Assert.False((await defaults.AuthorizeAsync(nameless, HrManagementPolicies.PolicyApprover)).Succeeded);
        Assert.True((await defaults.AuthorizeAsync(nameless, HrManagementPolicies.PolicyReader)).Succeeded);
    }

    [Theory]
    [InlineData("POST", "/ui/hr-core/PolicyTemplates/PublishFromUi", HrManagementPolicies.PolicyApprover)]
    [InlineData("POST", "/api/hr-core/api/policies/templates/0a000000-0000-4000-8000-000000000303/publish", HrManagementPolicies.PolicyApprover)]
    [InlineData("POST", "/ui/hr-core/PolicyTemplates/AddRule", HrManagementPolicies.PolicyAuthor)]
    [InlineData("POST", "/ui/hr-core/PolicyTemplates/CreateDraft", HrManagementPolicies.PolicyAuthor)]
    [InlineData("POST", "/ui/hr-core/PolicyTemplates/CreateRollbackDraft", HrManagementPolicies.PolicyAuthor)]
    [InlineData("POST", "/api/hr-core/api/policies/versions/0a000000-0000-4000-8000-000000000304/rules", HrManagementPolicies.PolicyAuthor)]
    [InlineData("DELETE", "/api/hr-core/api/policies/versions/0a000000-0000-4000-8000-000000000304/rules/0a000000-0000-4000-8000-000000000001", HrManagementPolicies.PolicyAuthor)]
    [InlineData("POST", "/api/hr-core/api/policies/templates/0a000000-0000-4000-8000-000000000301/drafts", HrManagementPolicies.PolicyAuthor)]
    [InlineData("POST", "/api/hr-core/api/policies/templates/0a000000-0000-4000-8000-000000000301/rollback-drafts", HrManagementPolicies.PolicyAuthor)]
    // PLAN-48: a draft of a domain's template (the scope rules) is the author's too.
    [InlineData("POST", "/api/hr-core/api/policies/drafts", HrManagementPolicies.PolicyAuthor)]
    [InlineData("GET", "/ui/hr-core/PolicyTemplates/Details/0a000000-0000-4000-8000-000000000301", HrManagementPolicies.PolicyReader)]
    [InlineData("GET", "/api/hr-core/api/policies/snapshots/Assets.Custody", HrManagementPolicies.PolicyReader)]
    [InlineData("POST", "/ui/hr-core/PolicyTemplates/Simulate", HrManagementPolicies.HrCoreWrite)]
    [InlineData("POST", "/api/hr-core/api/policies/evaluate", HrManagementPolicies.HrCoreWrite)]
    [InlineData("POST", "/api/hr-core/api/policies/templates/0a000000-0000-4000-8000-000000000303/simulate", HrManagementPolicies.HrCoreWrite)]
    [InlineData("POST", "/api/hr-core/api/internal/policies/enforce", HrManagementPolicies.HrCoreWrite)]
    public void The_gateway_sends_publishing_to_the_approver_and_drafts_to_the_author(string method, string path, string expected)
    {
        Assert.True(GatewayPathAuthorizationMiddleware.TryGetFineGrainedPolicy("hr-core", method, new Microsoft.AspNetCore.Http.PathString(path), out var policy));
        Assert.Equal(expected, policy);
    }

    [Fact]
    public void Every_policy_lifecycle_write_needs_the_author_or_approver_policy_and_antiforgery()
    {
        var actions = typeof(HR.Core.Web.Controllers.PoliciesController)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes(true).OfType<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Any())
            .ToList();
        Assert.NotEmpty(actions);

        // evaluate and simulate decide nothing durable and are called by services; every other write is the lifecycle's.
        var notLifecycle = new[] { nameof(HR.Core.Web.Controllers.PoliciesController.Evaluate), nameof(HR.Core.Web.Controllers.PoliciesController.Simulate) };
        var writes = 0;
        foreach (var action in actions.Where(a => !notLifecycle.Contains(a.Name)))
        {
            var verbs = action.GetCustomAttributes(true).OfType<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().SelectMany(a => a.HttpMethods);
            if (verbs.All(v => v == "GET"))
                continue;

            writes++;
            Assert.Contains(
                action.GetCustomAttributes(true).OfType<AuthorizeAttribute>(),
                a => a.Policy is HrManagementPolicies.PolicyAuthor or HrManagementPolicies.PolicyApprover);
            Assert.True(
                action.GetCustomAttributes(true).OfType<Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute>().Any(),
                $"{action.Name} has no [ValidateAntiForgeryToken]");
        }

        // PLAN-48: + POST api/policies/drafts (a draft of a domain's template, with the scope rules).
        Assert.Equal(9, writes);
    }
}
