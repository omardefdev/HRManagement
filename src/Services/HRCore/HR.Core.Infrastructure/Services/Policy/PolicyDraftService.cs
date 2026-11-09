using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

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
    Conflict,
    /// <summary>PLAN-48 (D-TEN-10): a national template asked for outside the national tenant (403).</summary>
    Forbidden
}

public sealed record PolicyDraftResult(PolicyDraftOutcome Outcome, string? MessageAr = null, IReadOnlyList<PolicyRuleIssue>? Issues = null, Guid? Id = null)
{
    public bool Succeeded => Outcome == PolicyDraftOutcome.Ok;

    public IReadOnlyList<PolicyRuleIssue> IssueList => Issues ?? [];

    public static PolicyDraftResult Ok(Guid? id = null) => new(PolicyDraftOutcome.Ok, Id: id);

    public static readonly PolicyDraftResult VersionNotFound = new(PolicyDraftOutcome.NotFound, "الإصدار غير موجود");

    public static readonly PolicyDraftResult VersionNotDraft = new(PolicyDraftOutcome.NotDraft, "الإصدار المنشور لا يُعدَّل: أنشئ مسودة جديدة");

    public static readonly PolicyDraftResult DraftExists = new(PolicyDraftOutcome.Conflict, "توجد مسودة لهذا القالب: أكملها أو تجاهلها أولاً");

    public const string NationalScopeForbiddenMessage =
        "لا تُنشأ مسودة وطنية خارج المستأجر الوطني: مسودة الجهة تجاوزٌ لها بقواعد أشد فقط (D-TEN-10، D-POL-4)";

    public static readonly PolicyDraftResult NationalScopeForbidden = new(PolicyDraftOutcome.Forbidden, NationalScopeForbiddenMessage);

    public static PolicyDraftResult Invalid(IReadOnlyList<PolicyRuleIssue> issues) =>
        new(PolicyDraftOutcome.Invalid, string.Join(" | ", issues.Select(i => $"{i.JsonPath}: {i.MessageAr}")), issues);

    public static PolicyDraftResult Invalid(string messageAr, string path = "$") =>
        Invalid([new PolicyRuleIssue("REQUEST", messageAr, path)]);
}

/// <summary>
/// PLAN-47 (G-POL-07, §5-§7): every change to a draft goes through here and is recorded in PolicyVersionEdits, so the publisher
/// can be proved not to be an editor. A draft starts as a copy of the effective version; its rules are validated before they are
/// saved; nothing here writes to a version that is not a draft.
/// <para>
/// PLAN-48 (§4): templates and versions are found through the national read (the session's own and the national ones), so a
/// write to a national row from an agency's session reaches <see cref="NationalSharedWriteGuard"/>, which refuses it (403); an
/// override's rules are validated with the agency ceiling (<see cref="PolicyRuleValidationContext.IsAgencyOverride"/>).
/// </para>
/// </summary>
public sealed class PolicyDraftService(HRCoreDbContext db, IPolicyRuleValidationService validation, IConfiguration configuration)
{
    public const int MaxReasonLength = 1000;

    /// <summary>
    /// PLAN-48 (§4, D-TEN-10): a draft of the domain's template in the session's tenant, a copy of its effective version. Outside
    /// the national tenant it is always the agency's override (TenantOverride), and a request for Scope = National is refused
    /// (<see cref="PolicyDraftOutcome.Forbidden"/>, 403); inside it the national template (the default), or the trial agency's
    /// override when asked. A missing template is created in the session's tenant.
    /// </summary>
    public async Task<PolicyDraftResult> CreateDomainDraftAsync(
        string? domain,
        string? subjectType,
        string? name,
        PolicyTemplateScope? scope,
        Guid? actorUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return PolicyDraftResult.Invalid("المجال مطلوب", "$.domain");
        // A known domain only, and values that fit their columns (a 400, never the database's 500).
        if (!PolicyDomains.All.Contains(domain, StringComparer.Ordinal))
            return PolicyDraftResult.Invalid($"المجال «{domain}» غير معروف", "$.domain");
        if (name is { Length: > 200 })
            return PolicyDraftResult.Invalid("الاسم أطول من 200 محرف", "$.name");
        if (subjectType is { Length: > 100 })
            return PolicyDraftResult.Invalid("نوع الموضوع أطول من 100 محرف", "$.subjectType");

        var sessionTenant = db.CurrentTenantId;
        // A session with no tenant (a platform operator outside a tenant) owns no template: 400, not the stamp's refusal (500).
        if (sessionTenant is null || sessionTenant == Guid.Empty)
            return PolicyDraftResult.Invalid("لا مستأجر لهذه الجلسة: ادخل مستأجراً قبل إنشاء مسودة", "$");
        var national = NationalTenant.IsNational(sessionTenant);
        var effectiveScope = scope ?? (national ? PolicyTemplateScope.National : PolicyTemplateScope.TenantOverride);
        if (effectiveScope == PolicyTemplateScope.National && !national)
            return PolicyDraftResult.NationalScopeForbidden;

        // The session's own template (the EF filter): the national one inside the national tenant, the agency's override outside.
        var template = await db.PolicyTemplates
            .Where(t => t.Domain == domain && t.Scope == effectiveScope)
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .FirstOrDefaultAsync(ct);
        if (template is null)
        {
            template = new PolicyTemplate
            {
                TenantId = sessionTenant ?? Guid.Empty,
                Domain = domain,
                Name = string.IsNullOrWhiteSpace(name) ? domain : name,
                SubjectType = subjectType ?? string.Empty,
                Scope = effectiveScope,
                Description = effectiveScope == PolicyTemplateScope.National ? "مسودة جديدة" : "تجاوز الجهة: قواعد أشد فقط (D-POL-4)"
            };
            db.PolicyTemplates.Add(template);
            await db.SaveChangesAsync(ct);
        }

        return await CreateDraftAsync(template.Id, copyFromVersionId: null, actorUserId, ct);
    }

    /// <summary>A new draft of the template: a copy of <paramref name="copyFromVersionId"/>, or of the effective version.</summary>
    public async Task<PolicyDraftResult> CreateDraftAsync(Guid templateId, Guid? copyFromVersionId, Guid? actorUserId, CancellationToken ct = default)
    {
        var template = await db.ReadablePolicyTemplates().Include(t => t.Versions).ThenInclude(v => v.Rules)
            .FirstOrDefaultAsync(t => t.Id == templateId, ct);
        if (template is null)
            return new(PolicyDraftOutcome.NotFound, "القالب غير موجود");
        if (template.Versions.Any(v => v.Status == PolicyVersionStatus.Draft))
            return PolicyDraftResult.DraftExists;

        PolicyTemplateVersion? source;
        if (copyFromVersionId is Guid fromId)
        {
            source = template.Versions.FirstOrDefault(v => v.Id == fromId && v.Status == PolicyVersionStatus.Published);
            if (source is null)
                return PolicyDraftResult.Invalid("الإصدار المنسوخ ليس إصداراً منشوراً لهذا القالب", "$.copyFromVersionId");
        }
        else
        {
            source = EffectiveVersion(template, DateTime.UtcNow);
        }

        return await AddDraftAsync(template, source, actorUserId, rollbackReason: null, rollbackOf: null, withdrawReason: null, ct);
    }

    /// <summary>§7: a draft that copies an earlier version, with a written reason. Publishing it is a four-eyes Rollback.</summary>
    public async Task<PolicyDraftResult> CreateRollbackDraftAsync(Guid templateId, Guid targetVersionId, string? reasonAr, Guid? actorUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reasonAr))
            return PolicyDraftResult.Invalid("التراجع يتطلب سبباً مكتوباً", "$.reason");
        if (reasonAr.Length > MaxReasonLength)
            return PolicyDraftResult.Invalid($"السبب أطول من {MaxReasonLength} محرف", "$.reason");

        var template = await db.ReadablePolicyTemplates().Include(t => t.Versions).ThenInclude(v => v.Rules)
            .FirstOrDefaultAsync(t => t.Id == templateId, ct);
        if (template is null)
            return new(PolicyDraftOutcome.NotFound, "القالب غير موجود");
        if (template.Versions.Any(v => v.Status == PolicyVersionStatus.Draft))
            return PolicyDraftResult.DraftExists;

        // Published versions only: an archived row is a discarded draft (or seeded history), never a decision that was in force.
        var target = template.Versions.FirstOrDefault(v => v.Id == targetVersionId && v.Status == PolicyVersionStatus.Published);
        if (target is null)
            return PolicyDraftResult.Invalid("الإصدار المستهدف ليس إصداراً منشوراً لهذا القالب", "$.targetVersionId");

        return await AddDraftAsync(template, target, actorUserId, reasonAr.Trim(), target.Id, withdrawReason: null, ct);
    }

    /// <summary>§5: the explicit "withdraw every rule of the domain": the only draft that may publish with no rule.</summary>
    public async Task<PolicyDraftResult> CreateWithdrawAllDraftAsync(Guid templateId, string? reasonAr, Guid? actorUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reasonAr))
            return PolicyDraftResult.Invalid("سحب كل قواعد المجال يتطلب سبباً مكتوباً", "$.reason");
        if (reasonAr.Length > MaxReasonLength)
            return PolicyDraftResult.Invalid($"السبب أطول من {MaxReasonLength} محرف", "$.reason");

        var template = await db.ReadablePolicyTemplates().Include(t => t.Versions).FirstOrDefaultAsync(t => t.Id == templateId, ct);
        if (template is null)
            return new(PolicyDraftOutcome.NotFound, "القالب غير موجود");
        if (template.Versions.Any(v => v.Status == PolicyVersionStatus.Draft))
            return PolicyDraftResult.DraftExists;

        return await AddDraftAsync(template, source: null, actorUserId, rollbackReason: null, rollbackOf: null, reasonAr.Trim(), ct);
    }

    public async Task<PolicyDraftResult> AddRuleAsync(Guid versionId, PolicyRuleInput input, Guid? actorUserId, CancellationToken ct = default)
    {
        var (version, error) = await LoadDraftAsync(versionId, ct);
        if (error is not null)
            return error;
        if (version!.WithdrawsAllRules)
            return PolicyDraftResult.Invalid("مسودة سحب كل القواعد لا تحمل قواعد", "$.ruleKey");

        var issues = await validation.ValidateRuleAsync(version.PolicyTemplate.Domain, version.PolicyTemplate.Scope, input, ct);
        if (issues.Count > 0)
            return PolicyDraftResult.Invalid(issues);
        if (version.Rules.Any(r => r.RuleKey == input.RuleKey))
            return PolicyDraftResult.Invalid($"المفتاح {input.RuleKey} موجود في هذه المسودة", "$.ruleKey");

        var rule = new PolicyRule
        {
            PolicyTemplateVersionId = versionId,
            TenantId = version.TenantId,
            RuleKey = input.RuleKey,
            Name = input.Name,
            Priority = input.Priority,
            WhenJson = input.WhenJson!,
            ThenJson = input.ThenJson!
        };
        db.PolicyRules.Add(rule);
        Record(version, actorUserId, PolicyVersionEditAction.AddRule, input.RuleKey);
        await db.SaveChangesAsync(ct);
        return PolicyDraftResult.Ok(rule.Id);
    }

    public async Task<PolicyDraftResult> UpdateRuleAsync(Guid versionId, Guid ruleId, PolicyRuleInput input, Guid? actorUserId, CancellationToken ct = default)
    {
        var (version, error) = await LoadDraftAsync(versionId, ct);
        if (error is not null)
            return error;

        var rule = version!.Rules.FirstOrDefault(r => r.Id == ruleId);
        if (rule is null)
            return new(PolicyDraftOutcome.NotFound, "القاعدة غير موجودة في هذه المسودة");

        var issues = await validation.ValidateRuleAsync(version.PolicyTemplate.Domain, version.PolicyTemplate.Scope, input, ct);
        if (issues.Count > 0)
            return PolicyDraftResult.Invalid(issues);
        if (version.Rules.Any(r => r.Id != ruleId && r.RuleKey == input.RuleKey))
            return PolicyDraftResult.Invalid($"المفتاح {input.RuleKey} موجود في هذه المسودة", "$.ruleKey");

        rule.RuleKey = input.RuleKey;
        rule.Name = input.Name;
        rule.Priority = input.Priority;
        rule.WhenJson = input.WhenJson!;
        rule.ThenJson = input.ThenJson!;
        Record(version, actorUserId, PolicyVersionEditAction.UpdateRule, input.RuleKey);
        await db.SaveChangesAsync(ct);
        return PolicyDraftResult.Ok(rule.Id);
    }

    public async Task<PolicyDraftResult> RemoveRuleAsync(Guid versionId, Guid ruleId, Guid? actorUserId, CancellationToken ct = default)
    {
        var (version, error) = await LoadDraftAsync(versionId, ct);
        if (error is not null)
            return error;

        var rule = version!.Rules.FirstOrDefault(r => r.Id == ruleId);
        if (rule is null)
            return new(PolicyDraftOutcome.NotFound, "القاعدة غير موجودة في هذه المسودة");

        db.PolicyRules.Remove(rule);
        Record(version, actorUserId, PolicyVersionEditAction.RemoveRule, rule.RuleKey);
        await db.SaveChangesAsync(ct);
        return PolicyDraftResult.Ok(rule.Id);
    }

    /// <summary>§6: when the draft takes effect once published; null means at publication. D-POL-10: not in the past.</summary>
    public async Task<PolicyDraftResult> SetEffectiveFromAsync(Guid versionId, DateTime? effectiveFromUtc, Guid? actorUserId, CancellationToken ct = default)
    {
        var (version, error) = await LoadDraftAsync(versionId, ct);
        if (error is not null)
            return error;

        DateTime? value = effectiveFromUtc is DateTime d ? DateTime.SpecifyKind(d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : d, DateTimeKind.Utc) : null;
        if (value is DateTime from
            && from < DateTime.UtcNow - PolicyPublishService.RetroactiveTolerance
            && !configuration.GetValue<bool>(PolicyPublishService.AllowRetroactiveKey))
            return PolicyDraftResult.Invalid(PolicyPublishService.RetroactiveMessage, "$.effectiveFrom");

        version!.EffectiveFrom = value;
        Record(version, actorUserId, PolicyVersionEditAction.SetEffectiveFrom, null);
        await db.SaveChangesAsync(ct);
        return PolicyDraftResult.Ok(version.Id);
    }

    /// <summary>§5: a draft that will not be published becomes Archived (kept for the record, never deleted).</summary>
    public async Task<PolicyDraftResult> DiscardDraftAsync(Guid versionId, Guid? actorUserId, CancellationToken ct = default)
    {
        var (version, error) = await LoadDraftAsync(versionId, ct);
        if (error is not null)
            return error;

        version!.Status = PolicyVersionStatus.Archived;
        Record(version, actorUserId, PolicyVersionEditAction.Discard, null);
        await db.SaveChangesAsync(ct);
        return PolicyDraftResult.Ok(version.Id);
    }

    /// <summary>The published version effective at <paramref name="asOfUtc"/>, the same choice the orchestrator makes.</summary>
    public static PolicyTemplateVersion? EffectiveVersion(PolicyTemplate template, DateTime asOfUtc) =>
        template.Versions
            .Where(v => v.Status == PolicyVersionStatus.Published)
            .Where(v => v.EffectiveFrom == null || v.EffectiveFrom <= asOfUtc)
            .Where(v => v.EffectiveTo == null || v.EffectiveTo > asOfUtc)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefault();

    private async Task<PolicyDraftResult> AddDraftAsync(
        PolicyTemplate template,
        PolicyTemplateVersion? source,
        Guid? actorUserId,
        string? rollbackReason,
        Guid? rollbackOf,
        string? withdrawReason,
        CancellationToken ct)
    {
        var draft = new PolicyTemplateVersion
        {
            PolicyTemplateId = template.Id,
            TenantId = template.TenantId,
            VersionNumber = (template.Versions.Count == 0 ? 0 : template.Versions.Max(v => v.VersionNumber)) + 1,
            Status = PolicyVersionStatus.Draft,
            CreatedBy = actorUserId,
            RollbackOfVersionId = rollbackOf,
            ChangeReason = rollbackReason ?? withdrawReason,
            WithdrawsAllRules = withdrawReason is not null
        };
        db.PolicyTemplateVersions.Add(draft);

        if (source is not null)
        {
            foreach (var rule in source.Rules.OrderBy(r => r.Priority).ThenBy(r => r.RuleKey, StringComparer.Ordinal))
            {
                db.PolicyRules.Add(new PolicyRule
                {
                    PolicyTemplateVersionId = draft.Id,
                    TenantId = template.TenantId,
                    RuleKey = rule.RuleKey,
                    Name = rule.Name,
                    Priority = rule.Priority,
                    WhenJson = rule.WhenJson,
                    ThenJson = rule.ThenJson
                });
            }
        }

        db.PolicyVersionEdits.Add(new PolicyVersionEdit
        {
            PolicyTemplateVersionId = draft.Id,
            TenantId = template.TenantId,
            UserId = actorUserId,
            Action = source is null ? PolicyVersionEditAction.Create : PolicyVersionEditAction.Copy,
            OccurredAt = DateTime.UtcNow
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ConcurrencyExtensions.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            return PolicyDraftResult.DraftExists;
        }

        return PolicyDraftResult.Ok(draft.Id);
    }

    /// <summary>
    /// The edit record, and a new revision of the draft: touching UpdatedAt updates the version row, so its xmin (the version's
    /// concurrency token) moves; a publication of a draft that changed after it was reviewed, and a change racing a
    /// publication, end in a conflict.
    /// </summary>
    private void Record(PolicyTemplateVersion version, Guid? actorUserId, PolicyVersionEditAction action, string? ruleKey)
    {
        version.UpdatedAt = DateTime.UtcNow;
        db.Entry(version).Property(v => v.UpdatedAt).IsModified = true;
        db.PolicyVersionEdits.Add(new PolicyVersionEdit
        {
            PolicyTemplateVersionId = version.Id,
            TenantId = version.TenantId,
            UserId = actorUserId,
            Action = action,
            RuleKey = ruleKey,
            OccurredAt = DateTime.UtcNow
        });
    }

    private async Task<(PolicyTemplateVersion? Version, PolicyDraftResult? Error)> LoadDraftAsync(Guid versionId, CancellationToken ct)
    {
        var version = await db.ReadablePolicyVersions()
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
