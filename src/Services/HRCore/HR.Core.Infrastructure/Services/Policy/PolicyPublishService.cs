using System.Security.Claims;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.ProdPolicy;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
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
    Conflict,
    /// <summary>PLAN-48: a national version published from an agency's session (403): national settings are read-only there.</summary>
    Forbidden
}

/// <summary>Who publishes: the user id from the token and the principal the approver policy is checked against.</summary>
public sealed record PolicyActor(Guid? UserId, ClaimsPrincipal? Principal);

/// <summary>
/// <paramref name="ExpectedRevision"/>: the draft's revision the approver reviewed (<see cref="PolicyPublishService.Revision"/>);
/// the page always sends it, an API client should.
/// </summary>
public sealed record PolicyPublishCommand(Guid VersionId, PolicyActor Publisher, string? EvidenceRef, uint? ExpectedRevision = null);

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
    // DI supplies all four. A seed applier built without a container (tests, tools) has none: the guards are then not called (the
    // validation still refuses an invalid version) and a manual publication is refused for want of an authorization service.
    IPolicyPlatformClaimGuard? guard = null,
    IAuthorizationService? authorization = null,
    IConfiguration? configuration = null,
    IHostEnvironment? environment = null)
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
            return await IsInheritedNationalAsync(command.VersionId, ct)
                ? new(PolicyPublishOutcome.Forbidden, NationalSharedWriteRefusedException.MessageAr)
                : new(PolicyPublishOutcome.NotFound, "الإصدار غير موجود");
        if (version.Status != PolicyVersionStatus.Draft)
            return new(PolicyPublishOutcome.NotDraft, "يمكن نشر المسودات فقط");

        // Four eyes (§2): an identified approver who is not among the editors.
        var publisherId = command.Publisher.UserId;
        if (publisherId is null || publisherId == Guid.Empty)
            return new(PolicyPublishOutcome.FourEyes, "four-eyes: الناشر يجب أن يكون مستخدماً معرّفاً");
        if (command.Publisher.Principal is null || authorization is null
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

        // The draft the approver reviewed (its revision, from the page or the API body) is the draft that is published: a change
        // after the review is a conflict, and so is a change that races this publication (the version's xmin).
        if (command.ExpectedRevision is uint expected && Revision(version) is uint actual && actual != expected)
            return new(PolicyPublishOutcome.Conflict, ChangedSinceReviewMessage);

        // The role the checker actually holds (the first of PolicyEngine:Roles:Approver); the author acted as policy-author.
        var checkerRole = PolicyEngineRoleHandler.ApproverRoles(configuration)
            .FirstOrDefault(r => command.Publisher.Principal.IsInRole(r)) ?? ApproverRoleName;
        var four = guard?.ValidateFourEyesPublish(AuthorRoleName, checkerRole);
        if (four is not null)
            return new(PolicyPublishOutcome.FourEyes, four);

        var makerId = version.CreatedBy ?? edits.FirstOrDefault(e => e.UserId.HasValue)?.UserId;
        var kind = version.RollbackOfVersionId is null ? PolicyPublicationKind.Manual : PolicyPublicationKind.Rollback;
        return await ValidateAndPublishAsync(version, new PolicyPublicationDetails(
            makerId, publisherId, command.EvidenceRef, kind, AuthorRoleName, checkerRole, version.RollbackOfVersionId), ct);
    }

    /// <summary>
    /// D-POL-11: a seed change set publishes without four eyes, as SystemSeed, with the set's id as the evidence, through the same
    /// publisher (and so the same IPolicyPublished) as any other publication. The validator checks the rules the set adds or
    /// replaces (<paramref name="changedRuleKeys"/>); the rules it carries over from the effective version are published as they
    /// were, so a rule PLAN-46 retired (left for PLAN-49) never fails the boot.
    /// </summary>
    public async Task<PolicyPublishResult> PublishSystemSeedAsync(Guid versionId, string changeSetId, IReadOnlySet<string> changedRuleKeys, CancellationToken ct = default)
    {
        var version = await LoadAsync(versionId, ct);
        if (version is null)
            return new(PolicyPublishOutcome.NotFound, "الإصدار غير موجود");
        return await PublishSystemSeedAsync(version, changeSetId, changedRuleKeys, ct);
    }

    /// <summary>
    /// PLAN-48 (P-6): the same, for a draft the caller holds (with its template and rules): an agency overlay pack, which the
    /// provisioning writes in the agency's tenant from another session. The validator applies the ceiling to an override.
    /// </summary>
    public async Task<PolicyPublishResult> PublishSystemSeedAsync(PolicyTemplateVersion version, string changeSetId, IReadOnlySet<string> changedRuleKeys, CancellationToken ct = default)
    {
        if (version.Status != PolicyVersionStatus.Draft)
            return new(PolicyPublishOutcome.NotDraft, "يمكن نشر المسودات فقط");

        return await ValidateAndPublishAsync(version, new PolicyPublicationDetails(
            null, null, changeSetId, PolicyPublicationKind.SystemSeed, SystemRoleName, SystemRoleName, null), ct, changedRuleKeys);
    }

    private async Task<PolicyPublishResult> ValidateAndPublishAsync(
        PolicyTemplateVersion version,
        PolicyPublicationDetails details,
        CancellationToken ct,
        IReadOnlySet<string>? onlyRuleKeys = null)
    {
        // PLAN-49 (D-POL-1): nothing is published in a domain the engine does not govern. The one exception is its retirement:
        // the final version with no rules, published by the retirement change set (never by a person).
        var retirement = details.Kind == PolicyPublicationKind.SystemSeed && version.WithdrawsAllRules && version.Rules.Count == 0;
        if (!retirement && validation.ValidateDomain(version.PolicyTemplate.Domain) is { Count: > 0 } outOfScope)
            return new(PolicyPublishOutcome.Invalid, outOfScope[0].MessageAr, outOfScope);

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
            var rollback = guard?.ValidateRollback(hasRollbackRef: true);
            if (rollback is not null)
                return PolicyPublishResult.Invalid(rollback);
        }

        // D-POL-10: no retroactive effect, unless the setting opens it.
        if (version.EffectiveFrom is DateTime from
            && from < DateTime.UtcNow - RetroactiveTolerance
            && configuration?.GetValue<bool>(AllowRetroactiveKey) != true)
            return PolicyPublishResult.Invalid(RetroactiveMessage, "$.effectiveFrom");

        // PLAN-48: an override's rules are checked against the agency ceiling too (D-TEN-8 = D-POL-4).
        var issues = await validation.ValidateVersionAsync(
            version.PolicyTemplate.Domain,
            version.PolicyTemplate.Scope,
            version.Rules
                .Where(r => onlyRuleKeys is null || (r.RuleKey is not null && onlyRuleKeys.Contains(r.RuleKey)))
                .OrderBy(r => r.Priority).ThenBy(r => r.RuleKey, StringComparer.Ordinal)
                .Select(PolicyRuleValidationService.ToInput).ToList(),
            ct);

        // PROD-001 publish gate: an invalid version is never published.
        var gate = guard?.ValidateInvalidPublish(isValid: issues.Count == 0, published: true);
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
        catch (DbUpdateException ex) when (ConcurrencyExtensions.IsUniqueViolation(ex) || IsPublishedMeanwhile(ex))
        {
            // The same race seen from another statement: the draft was published by the winner (the immutability trigger,
            // SQLSTATE P0047, refuses the loser's change of a version that is now published).
            db.ChangeTracker.Clear();
            return new(PolicyPublishOutcome.Conflict, ConcurrentPublishMessage);
        }

        return new(PolicyPublishOutcome.Ok, Version: version);
    }

    public const string ChangedSinceReviewMessage = "تغيّرت المسودة بعد مراجعتك؛ راجعها ثم انشرها";

    /// <summary>The draft's revision: its PostgreSQL xmin (every change of a draft updates the version row); null elsewhere.</summary>
    public uint? Revision(PolicyTemplateVersion version)
    {
        var property = db.Entry(version).Metadata.FindProperty("xmin");
        return property is null ? null : (uint?)db.Entry(version).Property("xmin").CurrentValue;
    }

    /// <summary>PLAN-47: the SQLSTATE the immutability triggers of POL047_PolicyRuleLifecycle raise.</summary>
    public const string ImmutabilitySqlState = "P0047";

    private static bool IsPublishedMeanwhile(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is Npgsql.PostgresException { SqlState: ImmutabilitySqlState })
                return true;
        return false;
    }

    private bool SameActorAllowedInDevelopment() =>
        environment?.IsDevelopment() == true && configuration?.GetValue<bool>(AllowSameActorInDevelopmentKey) == true;

    private Task<PolicyTemplateVersion?> LoadAsync(Guid versionId, CancellationToken ct) =>
        db.PolicyTemplateVersions
            .Include(v => v.PolicyTemplate)
            .Include(v => v.Rules)
            .FirstOrDefaultAsync(v => v.Id == versionId, ct);

    /// <summary>PLAN-48: a national version the agency's session reads (inherited) but never publishes.</summary>
    private async Task<bool> IsInheritedNationalAsync(Guid versionId, CancellationToken ct) =>
        !NationalTenant.IsNational(db.CurrentTenantId)
        && await db.ReadablePolicyVersions().AnyAsync(v => v.Id == versionId && v.TenantId == NationalTenant.Id, ct);
}
