using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Policy;

public enum PolicyDraftOutcome
{
    Ok,
    NotFound,
    /// <summary>The version is not a draft (409): a published version never changes.</summary>
    NotDraft,
    /// <summary>The rule or the request fails validation (400).</summary>
    Invalid,
    /// <summary>Another draft or publication got there first (409).</summary>
    Conflict
}

public sealed record PolicyDraftResult(PolicyDraftOutcome Outcome, string? MessageAr = null, IReadOnlyList<PolicyRuleIssue>? Issues = null, Guid? Id = null)
{
    public bool Succeeded => Outcome == PolicyDraftOutcome.Ok;

    public IReadOnlyList<PolicyRuleIssue> IssueList => Issues ?? [];

    public static PolicyDraftResult Ok(Guid? id = null) => new(PolicyDraftOutcome.Ok, Id: id);

    public static readonly PolicyDraftResult VersionNotFound = new(PolicyDraftOutcome.NotFound, "الإصدار غير موجود");

    public static readonly PolicyDraftResult VersionNotDraft = new(PolicyDraftOutcome.NotDraft, "الإصدار المنشور لا يُعدَّل: أنشئ مسودة جديدة");

    public static PolicyDraftResult Invalid(IReadOnlyList<PolicyRuleIssue> issues) =>
        new(PolicyDraftOutcome.Invalid, string.Join(" | ", issues.Select(i => $"{i.JsonPath}: {i.MessageAr}")), issues);

    public static PolicyDraftResult Invalid(string messageAr, string path = "$") =>
        Invalid([new PolicyRuleIssue("REQUEST", messageAr, path)]);
}

/// <summary>
/// PLAN-47 (G-POL-07): every change to a draft goes through here. A rule is validated before it is saved, and nothing here
/// writes to a version that is not a draft.
/// </summary>
public sealed class PolicyDraftService(HRCoreDbContext db, IPolicyRuleValidationService validation)
{
    public async Task<PolicyDraftResult> AddRuleAsync(Guid versionId, PolicyRuleInput input, Guid? actorUserId, CancellationToken ct = default)
    {
        var version = await LoadDraftAsync(versionId, ct);
        if (version.Result is not null)
            return version.Result;

        var issues = await validation.ValidateRuleAsync(version.Version!.PolicyTemplate.Domain, input, ct);
        if (issues.Count > 0)
            return PolicyDraftResult.Invalid(issues);

        if (version.Version.Rules.Any(r => r.RuleKey == input.RuleKey))
            return PolicyDraftResult.Invalid($"المفتاح {input.RuleKey} موجود في هذه المسودة", "$.ruleKey");

        var rule = new PolicyRule
        {
            PolicyTemplateVersionId = versionId,
            TenantId = version.Version.TenantId,
            RuleKey = input.RuleKey,
            Name = input.Name,
            Priority = input.Priority,
            WhenJson = input.WhenJson!,
            ThenJson = input.ThenJson!
        };
        db.PolicyRules.Add(rule);
        await db.SaveChangesAsync(ct);
        return PolicyDraftResult.Ok(rule.Id);
    }

    public async Task<PolicyDraftResult> UpdateRuleAsync(Guid versionId, Guid ruleId, PolicyRuleInput input, Guid? actorUserId, CancellationToken ct = default)
    {
        var version = await LoadDraftAsync(versionId, ct);
        if (version.Result is not null)
            return version.Result;

        var rule = version.Version!.Rules.FirstOrDefault(r => r.Id == ruleId);
        if (rule is null)
            return new PolicyDraftResult(PolicyDraftOutcome.NotFound, "القاعدة غير موجودة في هذه المسودة");

        var issues = await validation.ValidateRuleAsync(version.Version.PolicyTemplate.Domain, input, ct);
        if (issues.Count > 0)
            return PolicyDraftResult.Invalid(issues);

        if (version.Version.Rules.Any(r => r.Id != ruleId && r.RuleKey == input.RuleKey))
            return PolicyDraftResult.Invalid($"المفتاح {input.RuleKey} موجود في هذه المسودة", "$.ruleKey");

        rule.RuleKey = input.RuleKey;
        rule.Name = input.Name;
        rule.Priority = input.Priority;
        rule.WhenJson = input.WhenJson!;
        rule.ThenJson = input.ThenJson!;
        await db.SaveChangesAsync(ct);
        return PolicyDraftResult.Ok(rule.Id);
    }

    public async Task<PolicyDraftResult> RemoveRuleAsync(Guid versionId, Guid ruleId, Guid? actorUserId, CancellationToken ct = default)
    {
        var version = await LoadDraftAsync(versionId, ct);
        if (version.Result is not null)
            return version.Result;

        var rule = version.Version!.Rules.FirstOrDefault(r => r.Id == ruleId);
        if (rule is null)
            return new PolicyDraftResult(PolicyDraftOutcome.NotFound, "القاعدة غير موجودة في هذه المسودة");

        db.PolicyRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        return PolicyDraftResult.Ok(rule.Id);
    }

    private async Task<(PolicyTemplateVersion? Version, PolicyDraftResult? Result)> LoadDraftAsync(Guid versionId, CancellationToken ct)
    {
        var version = await db.PolicyTemplateVersions
            .Include(v => v.PolicyTemplate)
            .Include(v => v.Rules)
            .FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null)
            return (null, PolicyDraftResult.VersionNotFound);
        if (version.Status != PolicyVersionStatus.Draft)
            return (null, PolicyDraftResult.VersionNotDraft);
        return (version, null);
    }
}
