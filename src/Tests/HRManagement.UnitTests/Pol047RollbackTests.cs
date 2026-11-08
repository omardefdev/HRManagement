using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-47 §7: rollback is a new version, a copy of the target, published with four eyes as Rollback and linked to the target;
/// the reason is required; the validator checks the target's rules again.
/// </summary>
public sealed class Pol047RollbackTests
{
    private static readonly Guid Author = Guid.NewGuid();
    private static readonly Guid Approver = Guid.NewGuid();

    private static string RulesPrint(IEnumerable<PolicyRule> rules) =>
        string.Join("|", rules.OrderBy(r => r.RuleKey).Select(r => $"{r.RuleKey}:{r.Priority}:{r.WhenJson}:{r.ThenJson}"));

    [Fact]
    public async Task A_rollback_is_a_new_linked_version_with_the_rules_of_the_target()
    {
        await using var h = new Pol047Harness();
        var (template, v1) = await h.SeedTemplateAsync();
        var v2 = await h.DraftByAsync(template.Id, Author);
        Assert.True((await h.PublishService.PublishAsync(new(v2, Pol047Harness.Actor(Approver, HrManagementRoles.HrManagementApprover), null))).Succeeded);

        var draft = await h.Drafts.CreateRollbackDraftAsync(template.Id, v1.Id, "خطأ في القاعدة الجديدة", Author);
        Assert.True(draft.Succeeded, draft.MessageAr);
        var result = await h.PublishService.PublishAsync(new(draft.Id!.Value, Pol047Harness.Actor(Approver, HrManagementRoles.HrManagementApprover), "EV-RB"));
        Assert.True(result.Succeeded, result.MessageAr);

        var versions = await h.Db.PolicyTemplateVersions.AsNoTracking().Include(v => v.Rules).Where(v => v.PolicyTemplateId == template.Id).OrderBy(v => v.VersionNumber).ToListAsync();
        Assert.Equal([1, 2, 3], versions.Select(v => v.VersionNumber));
        Assert.Equal(RulesPrint(versions[0].Rules), RulesPrint(versions[2].Rules));
        Assert.Equal(v1.Id, versions[2].RollbackOfVersionId);
        Assert.Equal("خطأ في القاعدة الجديدة", versions[2].ChangeReason);
        var publication = await h.Db.PolicyPublications.SingleAsync(p => p.PolicyTemplateVersionId == versions[2].Id);
        Assert.Equal(PolicyPublicationKind.Rollback, publication.Kind);
        Assert.Equal(v1.Id, publication.RolledBackFromVersionId);
        Assert.Equal((Author, Approver), (publication.MakerUserId!.Value, publication.CheckerUserId!.Value));
        h.Guard.Verify(g => g.ValidateRollback(true), Times.Once);

        var effective = await new PolicyOrchestrator(h.Db, Mock.Of<HR.Core.Infrastructure.Services.Workflow.IWorkflowStarter>(), h.Cache,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PolicyOrchestrator>.Instance).GetPublishedSnapshotAsync(Pol047Harness.Domain, null);
        Assert.Equal(versions[2].Id, effective!.VersionId);
    }

    [Fact]
    public async Task A_rollback_needs_a_reason_and_four_eyes()
    {
        await using var h = new Pol047Harness();
        var (template, v1) = await h.SeedTemplateAsync();

        Assert.Equal(PolicyDraftOutcome.Invalid, (await h.Drafts.CreateRollbackDraftAsync(template.Id, v1.Id, "", Author)).Outcome);
        Assert.Equal(PolicyDraftOutcome.Invalid, (await h.Drafts.CreateRollbackDraftAsync(template.Id, Guid.NewGuid(), "سبب", Author)).Outcome);

        var draft = await h.Drafts.CreateRollbackDraftAsync(template.Id, v1.Id, "سبب", Author);
        var self = await h.PublishService.PublishAsync(new(draft.Id!.Value, Pol047Harness.Actor(Author, HrManagementRoles.HrManagementApprover), null));
        Assert.Equal(PolicyPublishOutcome.FourEyes, self.Outcome);

        // A discarded draft (Archived) was never in force: no rollback to it, no copy of it.
        Assert.True((await h.Drafts.DiscardDraftAsync(draft.Id.Value, Author)).Succeeded);
        Assert.Equal(PolicyDraftOutcome.Invalid, (await h.Drafts.CreateRollbackDraftAsync(template.Id, draft.Id.Value, "سبب", Author)).Outcome);
        Assert.Equal(PolicyDraftOutcome.Invalid, (await h.Drafts.CreateDraftAsync(template.Id, draft.Id.Value, Author)).Outcome);
    }

    [Fact]
    public async Task The_validator_checks_the_target_again_and_explains_the_refusal()
    {
        await using var h = new Pol047Harness(interceptor: false);
        await WorkflowPackSeeder.SeedAsync(h.Db);
        await PolicyEngineSeeder.SeedAsync(h.Db);
        // Movements.Transfer v1 holds R-TRF, which fires TRANSFER-APPROVAL, retired by PLAN-46 (left for PLAN-49).
        var template = await h.Db.PolicyTemplates.SingleAsync(t => t.Domain == PolicyDomains.MovementsTransfer);
        var v1 = await h.Db.PolicyTemplateVersions.SingleAsync(v => v.PolicyTemplateId == template.Id);

        var draft = await h.Drafts.CreateRollbackDraftAsync(template.Id, v1.Id, "إعادة الإصدار الأول", Author);
        Assert.True(draft.Succeeded, draft.MessageAr);
        var result = await h.PublishService.PublishAsync(new(draft.Id!.Value, Pol047Harness.Actor(Approver, HrManagementRoles.HrManagementApprover), null));

        Assert.Equal(PolicyPublishOutcome.Invalid, result.Outcome);
        var issue = Assert.Single(result.IssueList, i => i.Code == "THEN_WORKFLOW_RETIRED");
        Assert.Contains("TRANSFER-APPROVAL", issue.MessageAr);
        Assert.Contains("R-TRF", issue.MessageAr);
        Assert.Equal(PolicyVersionStatus.Draft, (await h.Db.PolicyTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == draft.Id)).Status);
    }
}
