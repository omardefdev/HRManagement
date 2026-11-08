using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-47 §3 (G-POL-07): a published version never changes through the application. The interceptor refuses any rule change of
/// a version that is not a draft and any change of a published version but EffectiveTo/UpdatedAt/UpdatedBy; drafts stay free.
/// </summary>
public sealed class Pol047ImmutabilityInterceptorTests
{
    private static PolicyRule NewRule(Guid versionId, string key) => new()
    {
        PolicyTemplateVersionId = versionId,
        TenantId = Pol047Harness.Tenant,
        RuleKey = key,
        Priority = 1,
        WhenJson = Pol047Harness.ValidWhen,
        ThenJson = Pol047Harness.ValidThen
    };

    [Fact]
    public async Task A_rule_of_a_published_version_cannot_be_added_changed_or_deleted()
    {
        await using var h = new Pol047Harness();
        var (_, v1) = await h.SeedTemplateAsync();
        h.Db.ChangeTracker.Clear();

        h.Db.PolicyRules.Add(NewRule(v1.Id, "R-SNEAK"));
        await Assert.ThrowsAsync<PolicyImmutabilityException>(() => h.Db.SaveChangesAsync());
        h.Db.ChangeTracker.Clear();

        var rule = await h.Db.PolicyRules.FirstAsync(r => r.PolicyTemplateVersionId == v1.Id);
        rule.WhenJson = "{}";
        Assert.Throws<PolicyImmutabilityException>(() => h.Db.SaveChanges());
        h.Db.ChangeTracker.Clear();

        rule = await h.Db.PolicyRules.FirstAsync(r => r.PolicyTemplateVersionId == v1.Id);
        h.Db.PolicyRules.Remove(rule);
        await Assert.ThrowsAsync<PolicyImmutabilityException>(() => h.Db.SaveChangesAsync());
        h.Db.ChangeTracker.Clear();

        Assert.Equal(1, await h.Db.PolicyRules.CountAsync(r => r.PolicyTemplateVersionId == v1.Id));
        Assert.Equal(Pol047Harness.ValidThen, (await h.Db.PolicyRules.SingleAsync(r => r.PolicyTemplateVersionId == v1.Id)).ThenJson);
    }

    [Fact]
    public async Task A_rule_cannot_move_from_a_draft_into_a_published_version()
    {
        await using var h = new Pol047Harness();
        var (template, v1) = await h.SeedTemplateAsync();
        var draftId = await h.DraftByAsync(template.Id, Guid.NewGuid());
        h.Db.ChangeTracker.Clear();

        var rule = await h.Db.PolicyRules.FirstAsync(r => r.PolicyTemplateVersionId == draftId);
        rule.PolicyTemplateVersionId = v1.Id;
        await Assert.ThrowsAsync<PolicyImmutabilityException>(() => h.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_published_version_changes_only_in_its_end_of_effect()
    {
        await using var h = new Pol047Harness();
        var (_, v1) = await h.SeedTemplateAsync();
        h.Db.ChangeTracker.Clear();

        var version = await h.Db.PolicyTemplateVersions.SingleAsync(v => v.Id == v1.Id);
        version.EffectiveTo = DateTime.UtcNow.AddDays(1);
        version.UpdatedBy = Guid.NewGuid();
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        foreach (var change in new Action<PolicyTemplateVersion>[]
                 {
                     v => v.EffectiveFrom = DateTime.UtcNow.AddYears(-5),
                     v => v.Status = PolicyVersionStatus.Archived,
                     v => v.Status = PolicyVersionStatus.Draft,
                     v => v.VersionNumber = 99,
                     v => v.ChangeReason = "تعديل صامت"
                 })
        {
            var target = await h.Db.PolicyTemplateVersions.SingleAsync(v => v.Id == v1.Id);
            change(target);
            await Assert.ThrowsAsync<PolicyImmutabilityException>(() => h.Db.SaveChangesAsync());
            h.Db.ChangeTracker.Clear();
        }

        var deleted = await h.Db.PolicyTemplateVersions.SingleAsync(v => v.Id == v1.Id);
        h.Db.PolicyTemplateVersions.Remove(deleted);
        await Assert.ThrowsAsync<PolicyImmutabilityException>(() => h.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_new_version_cannot_carry_rules_into_a_published_status_in_one_save()
    {
        await using var h = new Pol047Harness();
        var (template, _) = await h.SeedTemplateAsync();
        h.Db.ChangeTracker.Clear();

        var sneaky = new PolicyTemplateVersion { PolicyTemplateId = template.Id, TenantId = Pol047Harness.Tenant, VersionNumber = 5, Status = PolicyVersionStatus.Published };
        h.Db.PolicyTemplateVersions.Add(sneaky);
        h.Db.PolicyRules.Add(NewRule(sneaky.Id, "R-ONE-SAVE"));
        await Assert.ThrowsAsync<PolicyImmutabilityException>(() => h.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_draft_is_free_until_it_is_published_and_then_frozen()
    {
        await using var h = new Pol047Harness();
        var (template, _) = await h.SeedTemplateAsync();
        var draftId = await h.DraftByAsync(template.Id, Guid.NewGuid());
        h.Db.ChangeTracker.Clear();

        // Insert, update, delete on a draft.
        h.Db.PolicyRules.Add(NewRule(draftId, "R-EXTRA"));
        await h.Db.SaveChangesAsync();
        var extra = await h.Db.PolicyRules.SingleAsync(r => r.RuleKey == "R-EXTRA");
        extra.Priority = 7;
        await h.Db.SaveChangesAsync();
        h.Db.PolicyRules.Remove(extra);
        await h.Db.SaveChangesAsync();

        // Draft -> Published is a change of a draft row.
        var draft = await h.Db.PolicyTemplateVersions.SingleAsync(v => v.Id == draftId);
        draft.Status = PolicyVersionStatus.Published;
        draft.EffectiveFrom = DateTime.UtcNow;
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        h.Db.PolicyRules.Add(NewRule(draftId, "R-LATE"));
        await Assert.ThrowsAsync<PolicyImmutabilityException>(() => h.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_discarded_draft_is_archived_and_frozen()
    {
        await using var h = new Pol047Harness();
        var (template, _) = await h.SeedTemplateAsync();
        var draftId = await h.DraftByAsync(template.Id, Guid.NewGuid());
        Assert.True((await h.Drafts.DiscardDraftAsync(draftId, Guid.NewGuid())).Succeeded);
        h.Db.ChangeTracker.Clear();

        Assert.Equal(PolicyVersionStatus.Archived, (await h.Db.PolicyTemplateVersions.SingleAsync(v => v.Id == draftId)).Status);
        var rule = await h.Db.PolicyRules.FirstAsync(r => r.PolicyTemplateVersionId == draftId);
        rule.Name = "x";
        await Assert.ThrowsAsync<PolicyImmutabilityException>(() => h.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task The_draft_service_answers_409_style_for_a_published_version()
    {
        await using var h = new Pol047Harness();
        var (_, v1) = await h.SeedTemplateAsync();

        var result = await h.Drafts.AddRuleAsync(v1.Id, new("R-API", "x", 1, Pol047Harness.ValidWhen, Pol047Harness.ValidThen), Guid.NewGuid());

        Assert.Equal(HR.Core.Infrastructure.Services.Policy.PolicyDraftOutcome.NotDraft, result.Outcome);
        Assert.False(await h.Db.PolicyRules.AnyAsync(r => r.RuleKey == "R-API"));
    }
}
