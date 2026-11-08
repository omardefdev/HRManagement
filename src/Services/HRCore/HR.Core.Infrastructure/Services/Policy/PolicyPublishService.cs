using System.Security.Claims;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.ProdPolicy;
using HRManagement.SharedKernel.Policy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HR.Core.Infrastructure.Services.Policy;

public enum PolicyPublishOutcome
{
    Ok,
    NotFound,
    NotDraft,
    Invalid,
    /// <summary>The publisher is not an approver, is not identified, or edited the draft (403).</summary>
    FourEyes,
    /// <summary>Another publication of the same template won the race (409).</summary>
    Conflict
}

/// <summary>Who publishes: the user id from the token and the principal the approver policy is checked against.</summary>
public sealed record PolicyActor(Guid? UserId, ClaimsPrincipal? Principal);

public sealed record PolicyPublishCommand(Guid VersionId, PolicyActor Publisher, string? EvidenceRef);

public sealed record PolicyPublishResult(
    PolicyPublishOutcome Outcome,
    string? MessageAr = null,
    IReadOnlyList<PolicyRuleIssue>? Issues = null,
    PolicyTemplateVersion? Version = null)
{
    public bool Succeeded => Outcome == PolicyPublishOutcome.Ok;

    public IReadOnlyList<PolicyRuleIssue> IssueList => Issues ?? [];

    public static PolicyPublishResult Invalid(string messageAr, string path = "$") =>
        new(PolicyPublishOutcome.Invalid, messageAr, [new PolicyRuleIssue("PUBLISH", messageAr, path)]);
}

/// <summary>
/// PLAN-47 (G-POL-06, G-POL-07, §8): the only way a draft becomes published, for the API, the UI, rollback and seed change sets.
/// Validation, then four eyes, then the PROD-001 guards, then <see cref="IPolicyVersionPublisher"/>, which stages IPolicyPublished
/// in the same transaction (PLAN-40). This service never publishes the event itself.
/// </summary>
public sealed class PolicyPublishService(
    HRCoreDbContext db,
    IPolicyRuleValidationService validation,
    IPolicyVersionPublisher publisher,
    IPolicyPlatformClaimGuard guard,
    IAuthorizationService authorization,
    IConfiguration configuration,
    IHostEnvironment environment)
{
    public const string AllowSameActorInDevelopmentKey = "PolicyEngine:Publish:AllowSameActorInDevelopment";
    public const string AllowRetroactiveKey = "PolicyEngine:Publish:AllowRetroactive";

    /// <summary>D-POL-10: how far back an EffectiveFrom may be, without PolicyEngine:Publish:AllowRetroactive.</summary>
    public static readonly TimeSpan RetroactiveTolerance = TimeSpan.FromMinutes(5);

    public const string AuthorRoleName = "policy-author";
    public const string ApproverRoleName = "policy-approver";
    public const string SystemRoleName = "system";

    public const string ConcurrentPublishMessage = "نُشر إصدار آخر للتو؛ أعد المحاولة";
    public const string RetroactiveMessage = "النفاذ بأثر رجعي غير مسموح";

    public async Task<PolicyPublishResult> PublishAsync(PolicyPublishCommand command, CancellationToken ct = default)
    {
        var version = await LoadAsync(command.VersionId, ct);
        if (version is null)
            return new(PolicyPublishOutcome.NotFound, "الإصدار غير موجود");
        if (version.Status != PolicyVersionStatus.Draft)
            return new(PolicyPublishOutcome.NotDraft, "يمكن نشر المسودات فقط");

        // Four eyes (§2): an identified approver who is not among the editors.
        var publisherId = command.Publisher.UserId;
        if (publisherId is null || publisherId == Guid.Empty)
            return new(PolicyPublishOutcome.FourEyes, "four-eyes: الناشر يجب أن يكون مستخدماً معرّفاً");
        if (command.Publisher.Principal is null
            || !(await authorization.AuthorizeAsync(command.Publisher.Principal, HrManagementPolicies.PolicyApprover)).Succeeded)
            return new(PolicyPublishOutcome.FourEyes, "four-eyes: النشر لدور الاعتماد فقط");

        var edits = await db.PolicyVersionEdits.AsNoTracking()
            .Where(e => e.PolicyTemplateVersionId == version.Id)
            .OrderBy(e => e.OccurredAt)
            .Select(e => new { e.UserId, e.Action })
            .ToListAsync(ct);
        var editors = edits.Where(e => e.UserId.HasValue).Select(e => e.UserId!.Value).ToHashSet();
        if (version.CreatedBy is Guid creator)
            editors.Add(creator);
        if (editors.Count == 0 && edits.Count == 0)
            return PolicyPublishResult.Invalid("four-eyes: لا محرر مسجلاً لهذه المسودة، فلا يمكن إثبات أن الناشر غير المحرر");

        if (editors.Contains(publisherId.Value) && !SameActorAllowedInDevelopment())
            return new(PolicyPublishOutcome.FourEyes, "four-eyes: لا ينشر أحد مسودة حرّرها");

        var four = guard.ValidateFourEyesPublish(AuthorRoleName, ApproverRoleName);
        if (four is not null)
            return new(PolicyPublishOutcome.FourEyes, four);

        var makerId = version.CreatedBy ?? edits.FirstOrDefault(e => e.UserId.HasValue)?.UserId;
        var kind = version.RollbackOfVersionId is null ? PolicyPublicationKind.Manual : PolicyPublicationKind.Rollback;
        return await ValidateAndPublishAsync(version, new PolicyPublicationDetails(
            makerId, publisherId, command.EvidenceRef, kind, AuthorRoleName, ApproverRoleName, version.RollbackOfVersionId), ct);
    }

    /// <summary>
    /// D-POL-11: a seed change set publishes without four eyes, as SystemSeed, with the set's id as the evidence. The same
    /// validation and the same publisher (and so the same IPolicyPublished) as any other publication.
    /// </summary>
    public async Task<PolicyPublishResult> PublishSystemSeedAsync(Guid versionId, string changeSetId, CancellationToken ct = default)
    {
        var version = await LoadAsync(versionId, ct);
        if (version is null)
            return new(PolicyPublishOutcome.NotFound, "الإصدار غير موجود");
        if (version.Status != PolicyVersionStatus.Draft)
            return new(PolicyPublishOutcome.NotDraft, "يمكن نشر المسودات فقط");

        return await ValidateAndPublishAsync(version, new PolicyPublicationDetails(
            null, null, changeSetId, PolicyPublicationKind.SystemSeed, SystemRoleName, SystemRoleName, null), ct);
    }

    private async Task<PolicyPublishResult> ValidateAndPublishAsync(PolicyTemplateVersion version, PolicyPublicationDetails details, CancellationToken ct)
    {
        // §5: no empty version, except the explicit "withdraw every rule of the domain" draft with a written reason.
        if (version.WithdrawsAllRules)
        {
            if (string.IsNullOrWhiteSpace(version.ChangeReason))
                return PolicyPublishResult.Invalid("سحب كل قواعد المجال يتطلب سبباً مكتوباً", "$.changeReason");
            if (version.Rules.Count > 0)
                return PolicyPublishResult.Invalid("مسودة سحب كل القواعد يجب ألا تحمل قواعد", "$.rules");
        }
        else if (version.Rules.Count == 0)
        {
            return PolicyPublishResult.Invalid("لا يُنشر إصدار بلا قواعد إلا بإجراء «سحب كل قواعد المجال» بسبب مكتوب", "$.rules");
        }

        // §7: a rollback carries its reason, and the PROD-001 rollback gate is called.
        if (version.RollbackOfVersionId is not null)
        {
            if (string.IsNullOrWhiteSpace(version.ChangeReason))
                return PolicyPublishResult.Invalid("التراجع يتطلب سبباً مكتوباً", "$.changeReason");
            var rollback = guard.ValidateRollback(hasRollbackRef: true);
            if (rollback is not null)
                return PolicyPublishResult.Invalid(rollback);
        }

        // D-POL-10: no retroactive effect, unless the setting opens it.
        if (version.EffectiveFrom is DateTime from
            && from < DateTime.UtcNow - RetroactiveTolerance
            && !configuration.GetValue<bool>(AllowRetroactiveKey))
            return PolicyPublishResult.Invalid(RetroactiveMessage, "$.effectiveFrom");

        var issues = await validation.ValidateVersionAsync(
            version.PolicyTemplate.Domain,
            version.Rules.OrderBy(r => r.Priority).ThenBy(r => r.RuleKey, StringComparer.Ordinal).Select(PolicyRuleValidationService.ToInput).ToList(),
            ct);

        // PROD-001 publish gate: an invalid version is never published.
        var gate = guard.ValidateInvalidPublish(isValid: issues.Count == 0, published: true);
        if (issues.Count > 0)
            return new(PolicyPublishOutcome.Invalid, gate ?? "الإصدار لا يجتاز التحقق", issues);

        try
        {
            await publisher.PublishDraftAsync(version, details.MakerUserId, details.CheckerUserId, details.EvidenceRef, details, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return new(PolicyPublishOutcome.Conflict, ConcurrentPublishMessage);
        }
        catch (DbUpdateException ex) when (ConcurrencyExtensions.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            return new(PolicyPublishOutcome.Conflict, ConcurrentPublishMessage);
        }

        return new(PolicyPublishOutcome.Ok, Version: version);
    }

    private bool SameActorAllowedInDevelopment() =>
        environment.IsDevelopment() && configuration.GetValue<bool>(AllowSameActorInDevelopmentKey);

    private Task<PolicyTemplateVersion?> LoadAsync(Guid versionId, CancellationToken ct) =>
        db.PolicyTemplateVersions
            .Include(v => v.PolicyTemplate)
            .Include(v => v.Rules)
            .FirstOrDefaultAsync(v => v.Id == versionId, ct);
}
