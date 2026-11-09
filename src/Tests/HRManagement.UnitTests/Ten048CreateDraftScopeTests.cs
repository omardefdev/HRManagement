using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Web.Controllers;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Policy;
using HRManagement.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using static HRManagement.UnitTests.Ten048PolicyHarness;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-48 (§4, D-TEN-10, D-TEN-8 = D-POL-4): outside the national tenant a draft is the agency's override and a request for the
/// national scope answers 403, in the service, the API and the page; an agency's write to a national template or draft is refused
/// by <see cref="NationalSharedWriteGuard"/>; an override rule that would weaken the national policy is refused when it is added and
/// at publish (an old layer copied into a draft included); a national draft is never published from an agency.
/// </summary>
public sealed class Ten048CreateDraftScopeTests
{
    private const string DenyThen = """[{"effect":"Deny","params":{"reason":"agency"}}]""";

    [Fact]
    public async Task An_agency_asking_for_a_national_draft_gets_403_and_nothing_is_written()
    {
        await using var h = await CreateAsync();
        await using var db = h.Session(AgencyA);

        var result = await h.Drafts(db).CreateDomainDraftAsync(Domain, "Asset", "x", PolicyTemplateScope.National, Author);

        Assert.Equal(PolicyDraftOutcome.Forbidden, result.Outcome);
        Assert.Equal(PolicyDraftResult.NationalScopeForbiddenMessage, result.MessageAr);
        await using var check = h.DesignTime();
        Assert.False(await check.PolicyTemplates.AnyAsync(t => t.TenantId == AgencyA));
        Assert.Equal(0, await check.PolicyTemplateVersions.CountAsync(v => v.Status == PolicyVersionStatus.Draft));
    }

    [Theory]
    [InlineData("Not.A.Domain", "x", "Asset", "$.domain")]
    [InlineData(Domain, "a name longer than two hundred characters", "Asset", "$.name")]
    [InlineData(Domain, "x", "a subject type longer than one hundred characters", "$.subjectType")]
    public async Task A_draft_request_with_an_unknown_domain_or_an_oversized_value_is_a_400_and_writes_nothing(string domain, string name, string subjectType, string path)
    {
        await using var h = await CreateAsync();
        await using var db = h.Session(AgencyA);
        // The review's case: values that do not fit their columns ended in the database's 500.
        var longName = name.StartsWith("a name longer", StringComparison.Ordinal) ? new string('n', 201) : name;
        var longSubject = subjectType.StartsWith("a subject type", StringComparison.Ordinal) ? new string('s', 101) : subjectType;

        var result = await h.Drafts(db).CreateDomainDraftAsync(domain, longSubject, longName, null, Author);

        Assert.Equal(PolicyDraftOutcome.Invalid, result.Outcome);
        Assert.Contains(result.IssueList, i => i.JsonPath == path);
        await using var check = h.DesignTime();
        Assert.False(await check.PolicyTemplates.AnyAsync(t => t.TenantId == AgencyA));
    }

    [Fact]
    public async Task The_simulation_page_decides_with_the_agencys_own_override()
    {
        await using var h = await CreateAsync();
        var (_, version) = await h.SeedOldOverrideAsync(AgencyA,
            Rule("A-SIM-DENY", 1, DenyThen, """{"all":[{"field":"category","operator":"Equals","value":"T48-SIM"}]}"""));
        await using var db = h.Session(AgencyA);
        var controller = new PolicyTemplatesController(db, Orchestrator(db), h.Publish(db), h.Drafts(db), Pol047Harness.Authorization(h.Configuration))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Pol047Harness.User(Author, HrManagementRoles.HrCore) } }
        };

        var view = Assert.IsType<ViewResult>(await controller.Simulate(version.Id, """{"category":"T48-SIM","isPrivileged":"false","action":"Assign"}"""));

        // The review's case: the page asked for no tenant, so it showed the national-only decision.
        var decision = Assert.IsType<PolicyDecision>(view.ViewData["Decision"]);
        Assert.False(decision.Allowed);
        Assert.Contains("A-SIM-DENY", decision.MatchedRuleIds);
    }

    [Fact]
    public async Task An_agency_draft_is_its_own_override_and_the_national_template_is_untouched()
    {
        await using var h = await CreateAsync();
        var national = await h.NationalTemplateAsync();
        int nationalVersions;
        await using (var before = h.DesignTime())
            nationalVersions = await before.PolicyTemplateVersions.CountAsync(v => v.PolicyTemplateId == national.Id);

        await using var db = h.Session(AgencyA);
        var result = await h.Drafts(db).CreateDomainDraftAsync(Domain, "Asset", "custody override", scope: null, Author);

        Assert.True(result.Succeeded, result.MessageAr);
        await using var check = h.DesignTime();
        var draft = await check.PolicyTemplateVersions.Include(v => v.PolicyTemplate).SingleAsync(v => v.Id == result.Id);
        Assert.Equal(PolicyVersionStatus.Draft, draft.Status);
        Assert.Equal(AgencyA, draft.TenantId);
        Assert.Equal(AgencyA, draft.PolicyTemplate.TenantId);
        Assert.Equal(PolicyTemplateScope.TenantOverride, draft.PolicyTemplate.Scope);
        Assert.Equal(Domain, draft.PolicyTemplate.Domain);
        Assert.Equal(nationalVersions, await check.PolicyTemplateVersions.CountAsync(v => v.PolicyTemplateId == national.Id));
    }

    [Fact]
    public async Task Inside_the_national_tenant_the_draft_is_national_by_default_and_the_trial_agency_override_on_request()
    {
        await using var h = await CreateAsync();
        var national = await h.NationalTemplateAsync();

        await using (var db = h.Session(National))
        {
            var draft = await h.Drafts(db).CreateDomainDraftAsync(Domain, "Asset", "x", scope: null, Author);
            Assert.True(draft.Succeeded, draft.MessageAr);
            await using var check = h.DesignTime();
            Assert.Equal(national.Id, (await check.PolicyTemplateVersions.SingleAsync(v => v.Id == draft.Id)).PolicyTemplateId);
        }

        await using (var db = h.Session(National))
        {
            var trial = await h.Drafts(db).CreateDomainDraftAsync(Domain, "Asset", "trial", PolicyTemplateScope.TenantOverride, Author);
            Assert.True(trial.Succeeded, trial.MessageAr);
            await using var check = h.DesignTime();
            var template = (await check.PolicyTemplateVersions.Include(v => v.PolicyTemplate).SingleAsync(v => v.Id == trial.Id)).PolicyTemplate;
            Assert.Equal(PolicyTemplateScope.TenantOverride, template.Scope);
            Assert.Equal(National, template.TenantId);
        }
    }

    [Fact]
    public async Task A_draft_of_a_national_template_from_an_agency_is_refused_by_the_guard()
    {
        await using var h = await CreateAsync();
        var national = await h.NationalTemplateAsync();
        await using var db = h.Session(AgencyA);

        // The template is read (the national read), and the write is the guard's to refuse: not the stamp's generic refusal.
        var refused = await Assert.ThrowsAsync<NationalSharedWriteRefusedException>(() => h.Drafts(db).CreateDraftAsync(national.Id, null, Author));

        Assert.Equal(AgencyA, refused.SessionTenantId);
        await using var check = h.DesignTime();
        Assert.False(await check.PolicyTemplateVersions.AnyAsync(v => v.PolicyTemplateId == national.Id && v.Status == PolicyVersionStatus.Draft));
    }

    [Fact]
    public async Task A_rule_added_to_a_national_draft_from_an_agency_is_refused_by_the_guard_and_the_draft_is_unchanged()
    {
        await using var h = await CreateAsync();
        var national = await h.NationalTemplateAsync();
        Guid draftId;
        await using (var platform = h.Session(National))
        {
            var created = await h.Drafts(platform).CreateDraftAsync(national.Id, null, Author);
            Assert.True(created.Succeeded, created.MessageAr);
            draftId = created.Id!.Value;
        }

        int rules;
        await using (var before = h.DesignTime())
            rules = await before.PolicyRules.CountAsync(r => r.PolicyTemplateVersionId == draftId);

        await using var db = h.Session(AgencyA);
        await Assert.ThrowsAsync<NationalSharedWriteRefusedException>(() =>
            h.Drafts(db).AddRuleAsync(draftId, new PolicyRuleInput("A-INTO-NATIONAL", "x", 30, PrivilegedWhen, DenyThen), Author));

        await using var check = h.DesignTime();
        Assert.Equal(rules, await check.PolicyRules.CountAsync(r => r.PolicyTemplateVersionId == draftId));
        Assert.False(await check.PolicyRules.AnyAsync(r => r.RuleKey == "A-INTO-NATIONAL"));
    }

    [Theory]
    [InlineData("""[{"effect":"SelectCodePolicy","params":{"policyKey":"ASSET-PRIVILEGED"}}]""", PolicyCeiling.SelectCodePolicyCode)]
    [InlineData("""[{"effect":"SetField","params":{"field":"isPrivileged","value":"false"}}]""", PolicyCeiling.SetFieldCode)]
    [InlineData("""[{"effect":"TriggerWorkflow","params":{"workflowCode":"ASSET-ASSIGN-APPROVAL"}}]""", PolicyCeiling.TriggerWorkflowCode)]
    public async Task An_agency_rule_that_would_weaken_the_national_policy_is_refused_when_it_is_added(string thenJson, string code)
    {
        await using var h = await CreateAsync();
        await using var db = h.Session(AgencyA);
        var drafts = h.Drafts(db);
        var draft = await drafts.CreateDomainDraftAsync(Domain, "Asset", "override", null, Author);
        Assert.True(draft.Succeeded, draft.MessageAr);

        var refused = await drafts.AddRuleAsync(draft.Id!.Value, new PolicyRuleInput("A-WEAKER", "x", 1, PrivilegedWhen, thenJson), Author);

        Assert.Equal(PolicyDraftOutcome.Invalid, refused.Outcome);
        Assert.Contains(refused.IssueList, i => i.Code == code && i.JsonPath == "$.then[0].effect");

        // Deny and the stricter effects pass.
        var deny = await drafts.AddRuleAsync(draft.Id.Value, new PolicyRuleInput("A-DENY", "x", 1, PrivilegedWhen, DenyThen), Author);
        Assert.True(deny.Succeeded, deny.MessageAr);
        var dual = await drafts.AddRuleAsync(draft.Id.Value, new PolicyRuleInput("A-DUAL", "x", 2, PrivilegedWhen, """[{"effect":"RequireDualApproval"}]"""), Author);
        Assert.True(dual.Succeeded, dual.MessageAr);
    }

    [Fact]
    public async Task An_old_layer_copied_into_a_draft_is_refused_at_publish_and_the_ceiling_names_its_rule()
    {
        await using var h = await CreateAsync();
        var (template, _) = await h.SeedOldOverrideAsync(AgencyA,
            Rule("A-OLD-WF", 1, """[{"effect":"TriggerWorkflow","params":{"workflowCode":"ASSET-ASSIGN-APPROVAL"}}]"""));

        await using var db = h.Session(AgencyA);
        var draft = await h.Drafts(db).CreateDraftAsync(template.Id, null, Author);
        Assert.True(draft.Succeeded, draft.MessageAr);
        var added = await h.Drafts(db).AddRuleAsync(draft.Id!.Value, new PolicyRuleInput("A-DENY", "x", 2, PrivilegedWhen, DenyThen), Author);
        Assert.True(added.Succeeded, added.MessageAr);

        var result = await h.Publish(db).PublishAsync(new PolicyPublishCommand(draft.Id.Value, ApproverActor, "EV-48"));

        Assert.Equal(PolicyPublishOutcome.Invalid, result.Outcome);
        Assert.Contains(result.IssueList, i => i.Code == PolicyCeiling.TriggerWorkflowCode && i.MessageAr.Contains("A-OLD-WF"));
        await using var check = h.DesignTime();
        Assert.Equal(PolicyVersionStatus.Draft, (await check.PolicyTemplateVersions.SingleAsync(v => v.Id == draft.Id)).Status);
    }

    [Fact]
    public async Task An_agency_override_with_a_stricter_rule_publishes()
    {
        await using var h = await CreateAsync();
        await using var db = h.Session(AgencyA);
        var drafts = h.Drafts(db);
        var draft = await drafts.CreateDomainDraftAsync(Domain, "Asset", "override", null, Author);
        Assert.True(draft.Succeeded, draft.MessageAr);
        Assert.True((await drafts.AddRuleAsync(draft.Id!.Value, new PolicyRuleInput("A-DENY", "x", 1, PrivilegedWhen, DenyThen), Author)).Succeeded);

        var result = await h.Publish(db).PublishAsync(new PolicyPublishCommand(draft.Id.Value, ApproverActor, "EV-48"));

        Assert.True(result.Succeeded, result.MessageAr);
        Assert.Equal(AgencyA, result.Version!.TenantId);
    }

    [Fact]
    public async Task A_national_draft_is_never_published_from_an_agency()
    {
        await using var h = await CreateAsync();
        var national = await h.NationalTemplateAsync();
        Guid draftId;
        await using (var platform = h.Session(National))
        {
            var created = await h.Drafts(platform).CreateDraftAsync(national.Id, null, Author);
            Assert.True(created.Succeeded, created.MessageAr);
            draftId = created.Id!.Value;
        }

        await using var db = h.Session(AgencyA);
        var result = await h.Publish(db).PublishAsync(new PolicyPublishCommand(draftId, ApproverActor, "EV-48"));

        Assert.Equal(PolicyPublishOutcome.Forbidden, result.Outcome);
        Assert.Equal(NationalSharedWriteRefusedException.MessageAr, result.MessageAr);
        await using var check = h.DesignTime();
        Assert.Equal(PolicyVersionStatus.Draft, (await check.PolicyTemplateVersions.SingleAsync(v => v.Id == draftId)).Status);
    }

    [Fact]
    public async Task The_api_answers_403_for_a_national_draft_from_an_agency_and_400_for_an_unknown_scope()
    {
        await using var h = await CreateAsync();
        await using var db = h.Session(AgencyA);
        var controller = new PoliciesController(db, Mock.Of<IPolicyOrchestrator>(), h.Publish(db), h.Drafts(db))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Pol047Harness.User(Author, HrManagementRoles.PolicyAuthor) } }
        };

        var national = await controller.CreateDomainDraft(new CreateDomainDraftRequest { Domain = Domain, Scope = "National" }, default);
        var forbidden = Assert.IsType<ObjectResult>(national);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);

        Assert.IsType<BadRequestObjectResult>(await controller.CreateDomainDraft(new CreateDomainDraftRequest { Domain = Domain, Scope = "Everywhere" }, default));

        var own = await controller.CreateDomainDraft(new CreateDomainDraftRequest { Domain = Domain, SubjectType = "Asset", Name = "override" }, default);
        Assert.IsType<OkObjectResult>(own);
    }

    [Fact]
    public async Task The_page_answers_403_for_a_national_draft_from_an_agency()
    {
        await using var h = await CreateAsync();
        await using var db = h.Session(AgencyA);
        var controller = new PolicyTemplatesController(db, Mock.Of<IPolicyOrchestrator>(), h.Publish(db), h.Drafts(db),
            Pol047Harness.Authorization(h.Configuration))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Pol047Harness.User(Author, HrManagementRoles.PolicyAuthor) } }
        };

        var result = await controller.CreateDraft(Domain, "Asset", "x", "National");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, view.StatusCode);
        Assert.Equal(PolicyDraftResult.NationalScopeForbiddenMessage, view.Model);
    }

    [Fact]
    public async Task The_page_answers_403_for_a_national_publication_from_an_agency()
    {
        await using var h = await CreateAsync();
        var national = await h.NationalTemplateAsync();
        Guid draftId;
        await using (var platform = h.Session(National))
            draftId = (await h.Drafts(platform).CreateDraftAsync(national.Id, null, Author)).Id!.Value;

        await using var db = h.Session(AgencyA);
        var controller = new PolicyTemplatesController(db, Mock.Of<IPolicyOrchestrator>(), h.Publish(db), h.Drafts(db),
            Pol047Harness.Authorization(h.Configuration))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Pol047Harness.User(Approver, HrManagementRoles.HrManagementApprover) } }
        };

        var view = Assert.IsType<ViewResult>(await controller.PublishFromUi(draftId, "EV-48", null));

        Assert.Equal(StatusCodes.Status403Forbidden, view.StatusCode);
        Assert.Equal(NationalSharedWriteRefusedException.MessageAr, view.Model);
    }

    [Fact]
    public void The_host_runs_the_national_guard_before_the_tenant_stamp()
    {
        var program = global::System.IO.File.ReadAllText(RepoPaths.Resolve("src", "Services", "HRCore", "HR.Core.Web", "Program.cs"));
        var guard = program.IndexOf("sp.GetRequiredService<NationalSharedWriteGuard>()", StringComparison.Ordinal);
        var stamp = program.IndexOf("sp.GetRequiredService<TenantIdStampInterceptor>()", StringComparison.Ordinal);
        Assert.True(guard > 0 && stamp > guard, "NationalSharedWriteGuard must be registered before TenantIdStampInterceptor in HR.Core's AddInterceptors");
    }
}
