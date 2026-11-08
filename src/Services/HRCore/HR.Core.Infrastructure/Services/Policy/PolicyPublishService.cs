using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults.ProdPolicy;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Policy;

public enum PolicyPublishOutcome
{
    Ok,
    NotFound,
    NotDraft,
    Invalid,
    FourEyes,
    Conflict
}

public sealed record PolicyPublishCommand(Guid VersionId, Guid? MakerUserId, Guid? CheckerUserId, string? EvidenceRef);

public sealed record PolicyPublishResult(
    PolicyPublishOutcome Outcome,
    string? MessageAr = null,
    IReadOnlyList<PolicyRuleIssue>? Issues = null,
    PolicyTemplateVersion? Version = null)
{
    public bool Succeeded => Outcome == PolicyPublishOutcome.Ok;

    public IReadOnlyList<PolicyRuleIssue> IssueList => Issues ?? [];
}

/// <summary>
/// PLAN-47 (G-POL-07, §8): the only way a draft becomes published, for the API, the UI, rollback and seed change sets. It
/// validates the whole version and then hands it to <see cref="IPolicyVersionPublisher"/>, which stages IPolicyPublished in the
/// same transaction (PLAN-40); this service never publishes the event itself.
/// </summary>
public sealed class PolicyPublishService(
    HRCoreDbContext db,
    IPolicyRuleValidationService validation,
    IPolicyVersionPublisher publisher,
    IPolicyPlatformClaimGuard guard)
{
    public async Task<PolicyPublishResult> PublishAsync(PolicyPublishCommand command, CancellationToken ct = default)
    {
        var version = await db.PolicyTemplateVersions
            .Include(v => v.PolicyTemplate)
            .Include(v => v.Rules)
            .FirstOrDefaultAsync(v => v.Id == command.VersionId, ct);
        if (version is null)
            return new(PolicyPublishOutcome.NotFound, "الإصدار غير موجود");
        if (version.Status != PolicyVersionStatus.Draft)
            return new(PolicyPublishOutcome.NotDraft, "يمكن نشر المسودات فقط");

        var issues = await validation.ValidateVersionAsync(
            version.PolicyTemplate.Domain,
            version.Rules.OrderBy(r => r.Priority).ThenBy(r => r.RuleKey, StringComparer.Ordinal).Select(PolicyRuleValidationService.ToInput).ToList(),
            ct);

        // PROD-001 publish gate: an invalid version is never published.
        var gate = guard.ValidateInvalidPublish(isValid: issues.Count == 0, published: true);
        if (issues.Count > 0)
            return new(PolicyPublishOutcome.Invalid, gate ?? "الإصدار لا يجتاز التحقق", issues);

        await publisher.PublishDraftAsync(version, command.MakerUserId, command.CheckerUserId, command.EvidenceRef, ct);
        return new(PolicyPublishOutcome.Ok, Version: version);
    }
}
