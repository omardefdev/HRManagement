using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Services.Policy;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-47 §5 (G-POL-07): a draft starts as a copy of the effective version; its rules are added, changed and removed (validated,
/// recorded); one draft per template; a discarded draft is archived; no empty version is published but the explicit withdrawal.
/// </summary>
public sealed class Pol047DraftServiceTests
{
    private static readonly Guid Author = Guid.NewGuid();
    private static readonly Guid Approver = Guid.NewGuid();

    [Fact]
    public async Task A_draft_is_a_copy_of_the_effective_version()
    {
        await using var h = new Pol047Harness();
        var (template, v1) = await h.SeedTemplateAsync();

        var created = await h.Drafts.CreateDraftAsync(template.Id, null, Author);

        Assert.True(created.Succeeded, created.MessageAr);
        var draft = await h.Db.PolicyTemplateVersions.AsNoTracking().Include(v => v.Rules).SingleAsync(v => v.Id == created.Id);
        Assert.Equal(PolicyVersionStatus.Draft, draft.Status);
        Assert.Equal(2, draft.VersionNumber);
        Assert.Equal(Author, draft.CreatedBy);
        var copied = Assert.Single(draft.Rules);
        var original = await h.Db.PolicyRules.AsNoTracking().SingleAsync(r => r.PolicyTemplateVersionId == v1.Id);
        Assert.NotEqual(original.Id, copied.Id);
        Assert.Equal((original.RuleKey, original.WhenJson, original.ThenJson, original.Priority), (copied.RuleKey, copied.WhenJson, copied.ThenJson, copied.Priority));
        var edit = await h.Db.PolicyVersionEdits.SingleAsync(e => e.PolicyTemplateVersionId == draft.Id);
        Assert.Equal((Author, PolicyVersionEditAction.Copy), (edit.UserId!.Value, edit.Action));
    }

    [Fact]
    public async Task Rules_of_a_draft_are_changed_and_removed_in_place()
    {
        await using var h = new Pol047Harness();
        var (template, _) = await h.SeedTemplateAsync();
        var draftId = await h.DraftByAsync(template.Id, Author);
        var copied = await h.Db.PolicyRules.AsNoTracking().SingleAsync(r => r.PolicyTemplateVersionId == draftId && r.RuleKey == "R-V1");

        var updated = await h.Drafts.UpdateRuleAsync(draftId, copied.Id, new PolicyRuleInput("R-V1", "معدلة", 3, Pol047Harness.ValidWhen, Pol047Harness.ValidThen), Author);
        Assert.True(updated.Succeeded, updated.MessageAr);
        var removed = await h.Drafts.RemoveRuleAsync(draftId, copied.Id, Author);
        Assert.True(removed.Succeeded, removed.MessageAr);

        var rules = await h.Db.PolicyRules.AsNoTracking().Where(r => r.PolicyTemplateVersionId == draftId).ToListAsync();
        Assert.Equal(["R-NEW"], rules.Select(r => r.RuleKey));
        var actions = await h.Db.PolicyVersionEdits.Where(e => e.PolicyTemplateVersionId == draftId).OrderBy(e => e.OccurredAt).Select(e => e.Action).ToListAsync();
        Assert.Equal([PolicyVersionEditAction.Copy, PolicyVersionEditAction.AddRule, PolicyVersionEditAction.UpdateRule, PolicyVersionEditAction.RemoveRule], actions);
    }

    [Fact]
    public async Task An_invalid_rule_is_refused_with_its_path_and_nothing_is_saved()
    {
        await using var h = new Pol047Harness();
        var (template, _) = await h.SeedTemplateAsync();
        var draftId = await h.DraftByAsync(template.Id, Author);
        var before = await h.Db.PolicyRules.CountAsync();

        foreach (var (when, then, path) in new[]
                 {
                     ("""{"when":{"all":[{"field":"category","operator":"Equals","value":"x"}]}}""", Pol047Harness.ValidThen, "$.when.when"),
                     ("""{"all":[{"field":"value","operator":"LessThanOrEqualz","value":"1"}]}""", Pol047Harness.ValidThen, "$.when.all[0].operator"),
                     ("""{"all":[{"field":"employeeName","operator":"Equals","value":"x"}]}""", Pol047Harness.ValidThen, "$.when.all[0].field"),
                     (Pol047Harness.ValidWhen, """[{"effect":"TriggerWorkflow","params":{"workflowCode":"NOPE"}}]""", "$.then[0].params.workflowCode"),
                     (Pol047Harness.ValidWhen, "[]", "$.then")
                 })
        {
            var result = await h.Drafts.AddRuleAsync(draftId, new PolicyRuleInput("R-BAD", "x", 5, when, then), Author);
            Assert.Equal(PolicyDraftOutcome.Invalid, result.Outcome);
            Assert.Contains(result.IssueList, i => i.JsonPath == path);
            Assert.Matches("[؀-ۿ]", result.MessageAr);
        }

        Assert.Equal(before, await h.Db.PolicyRules.CountAsync());
        var duplicate = await h.Drafts.AddRuleAsync(draftId, new PolicyRuleInput("R-NEW", "x", 5, Pol047Harness.ValidWhen, Pol047Harness.ValidThen), Author);
        Assert.Equal(PolicyDraftOutcome.Invalid, duplicate.Outcome);
    }

    [Fact]
    public async Task One_draft_per_template()
    {
        await using var h = new Pol047Harness();
        var (template, _) = await h.SeedTemplateAsync();
        Assert.True((await h.Drafts.CreateDraftAsync(template.Id, null, Author)).Succeeded);

        var second = await h.Drafts.CreateDraftAsync(template.Id, null, Guid.NewGuid());
        Assert.Equal(PolicyDraftOutcome.Conflict, second.Outcome);

        // The database index refuses it too, whatever path writes it.
        h.Db.PolicyTemplateVersions.Add(new PolicyTemplateVersion { PolicyTemplateId = template.Id, TenantId = Pol047Harness.Tenant, VersionNumber = 7, Status = PolicyVersionStatus.Draft });
        await Assert.ThrowsAsync<DbUpdateException>(() => h.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_discarded_draft_frees_the_template_for_a_new_one()
    {
        await using var h = new Pol047Harness();
        var (template, _) = await h.SeedTemplateAsync();
        var draftId = await h.DraftByAsync(template.Id, Author);

        Assert.True((await h.Drafts.DiscardDraftAsync(draftId, Author)).Succeeded);
        var next = await h.Drafts.CreateDraftAsync(template.Id, null, Author);

        Assert.True(next.Succeeded, next.MessageAr);
        Assert.Equal(3, (await h.Db.PolicyTemplateVersions.SingleAsync(v => v.Id == next.Id)).VersionNumber);
        Assert.Equal(PolicyVersionStatus.Archived, (await h.Db.PolicyTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == draftId)).Status);
    }

    [Fact]
    public async Task An_empty_version_is_published_only_as_the_explicit_withdrawal_with_a_reason()
    {
        await using var h = new Pol047Harness();
        var (template, v1) = await h.SeedTemplateAsync();
        var draftId = await h.DraftByAsync(template.Id, Author);
        foreach (var rule in await h.Db.PolicyRules.AsNoTracking().Where(r => r.PolicyTemplateVersionId == draftId).ToListAsync())
            Assert.True((await h.Drafts.RemoveRuleAsync(draftId, rule.Id, Author)).Succeeded);

        var empty = await h.PublishService.PublishAsync(new(draftId, Pol047Harness.Actor(Approver, HrManagementRoles.HrManagementApprover), null));
        Assert.Equal(PolicyPublishOutcome.Invalid, empty.Outcome);
        Assert.Contains(empty.IssueList, i => i.JsonPath == "$.rules");

        Assert.True((await h.Drafts.DiscardDraftAsync(draftId, Author)).Succeeded);
        Assert.Equal(PolicyDraftOutcome.Invalid, (await h.Drafts.CreateWithdrawAllDraftAsync(template.Id, " ", Author)).Outcome);
        var withdrawal = await h.Drafts.CreateWithdrawAllDraftAsync(template.Id, "إلغاء القواعد بقرار الوزارة", Author);
        Assert.True(withdrawal.Succeeded, withdrawal.MessageAr);
        Assert.Equal(PolicyDraftOutcome.Invalid, (await h.Drafts.AddRuleAsync(withdrawal.Id!.Value, new("R-X", "x", 1, Pol047Harness.ValidWhen, Pol047Harness.ValidThen), Author)).Outcome);

        var published = await h.PublishService.PublishAsync(new(withdrawal.Id.Value, Pol047Harness.Actor(Approver, HrManagementRoles.HrManagementApprover), null));
        Assert.True(published.Succeeded, published.MessageAr);
        Assert.NotNull((await h.Db.PolicyTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == v1.Id)).EffectiveTo);
    }

    [Fact]
    public async Task A_published_version_answers_not_a_draft_for_every_change()
    {
        await using var h = new Pol047Harness();
        var (_, v1) = await h.SeedTemplateAsync();
        var rule = await h.Db.PolicyRules.AsNoTracking().SingleAsync(r => r.PolicyTemplateVersionId == v1.Id);

        Assert.Equal(PolicyDraftOutcome.NotDraft, (await h.Drafts.UpdateRuleAsync(v1.Id, rule.Id, new("R-V1", "x", 1, Pol047Harness.ValidWhen, Pol047Harness.ValidThen), Author)).Outcome);
        Assert.Equal(PolicyDraftOutcome.NotDraft, (await h.Drafts.RemoveRuleAsync(v1.Id, rule.Id, Author)).Outcome);
        Assert.Equal(PolicyDraftOutcome.NotDraft, (await h.Drafts.SetEffectiveFromAsync(v1.Id, DateTime.UtcNow.AddDays(1), Author)).Outcome);
        Assert.Equal(PolicyDraftOutcome.NotDraft, (await h.Drafts.DiscardDraftAsync(v1.Id, Author)).Outcome);
    }
}
