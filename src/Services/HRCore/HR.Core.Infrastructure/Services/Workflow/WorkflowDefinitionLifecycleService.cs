using System.Text.Json;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services.Workflow;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Workflow;

/// <summary>Who acts on a definition, and from which tenant.</summary>
public sealed record WorkflowDefinitionActor(Guid? UserId, Guid? TenantId);

/// <summary>What a draft may set. A null field on an update leaves the value as it is.</summary>
public sealed record WorkflowDefinitionChange
{
    public string? Code { get; init; }
    public string? Name { get; init; }
    public string? DomainEntityType { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<WorkflowStepSnapshot>? Steps { get; init; }
    public IReadOnlyList<string>? GuardrailRoles { get; init; }
    public DateTime? ValidFrom { get; init; }
    public DateTime? ValidTo { get; init; }
    public bool? RequireDistinctApprovers { get; init; }
    public string? ChangeReason { get; init; }
}

public enum WorkflowDefinitionFailure
{
    None,
    NotFound,
    Forbidden,
    Invalid
}

public sealed record WorkflowDefinitionResult(
    WorkflowDefinition? Definition,
    WorkflowDefinitionFailure Failure = WorkflowDefinitionFailure.None,
    IReadOnlyList<string>? Errors = null,
    IReadOnlyList<string>? Warnings = null)
{
    public bool Success => Failure == WorkflowDefinitionFailure.None;
}

/// <summary>
/// PLAN-43 (G-WF-04, D-WF-4): the only writer of definitions outside the seeder. Draft → PendingPublish → Published → Retired, by
/// the national tenant, with a second person and the validator at publication. A published version is never edited: a change is a
/// new version, and running instances stay on the version they pinned. <c>AuditSaveChangesInterceptor</c> records each transition;
/// the fields keep the actor. Two transitions racing on one row end in a concurrency conflict (<c>xmin</c>).
/// </summary>
public sealed class WorkflowDefinitionLifecycleService(HRCoreDbContext db)
{
    public const string NationalOnlyMessage = "إدارة التعريفات وطنية في هذا الإصدار.";
    public const string SecondPersonMessage = "لا يجوز لمنشئ التعريف أو مقدّمه نشره.";
    public const string NotDraftMessage = "لا يُعدَّل التعريف إلا وهو مسودة. أنشئ إصدارًا جديدًا بدلاً من ذلك.";
    public const string NotPendingMessage = "التعريف ليس بانتظار النشر.";
    public const string NotPublishedMessage = "لا يُسحب إلا إصدار منشور.";
    public const string RetireReasonMessage = "سبب السحب مطلوب (10 أحرف على الأقل).";
    public const string LastPublishedMessage = "لا يمكن سحب آخر إصدار منشور لهذا المسار قبل نشر إصدار بديل.";
    public const string EntityTypeChangedMessage = "نوع الكيان يجب أن يطابق الإصدارات الأخرى لهذا الرمز.";
    public const string ValidityMessage = "نهاية السريان يجب أن تكون بعد بدايته.";
    public const string ActorMessage = "تعذّر تحديد المستخدم.";

    public const int MinRetireReasonLength = 10;
    public const int MaxChangeReasonLength = 500;

    public static string BindingWarning(int pinnedVersion) =>
        $"الربط يثبّت الإصدار {pinnedVersion}؛ الإصدار الجديد لن يُستعمل في مسار السياسة حتى يُحدَّث الربط.";

    public async Task<WorkflowDefinitionResult> CreateDraftAsync(
        WorkflowDefinitionChange change, WorkflowDefinitionActor actor, CancellationToken ct = default)
    {
        if (Refuse(actor) is { } refused)
            return refused;

        var code = change.Code?.Trim() ?? string.Empty;
        if (code.Length == 0)
            return Invalid("رمز التعريف مطلوب.");

        var existingMax = await db.WorkflowDefinitions
            .Where(d => d.Code == code)
            .MaxAsync(d => (int?)d.Version, ct) ?? 0;

        var definition = new WorkflowDefinition
        {
            TenantId = TenantDefaults.DefaultTenantId,
            Code = code,
            Name = change.Name ?? string.Empty,
            Version = existingMax + 1,
            DomainEntityType = change.DomainEntityType ?? string.Empty,
            Description = change.Description,
            StepsJson = JsonSerializer.Serialize(change.Steps ?? []),
            GuardrailRolesJson = JsonSerializer.Serialize(change.GuardrailRoles ?? []),
            ValidFrom = change.ValidFrom,
            ValidTo = change.ValidTo,
            Status = WorkflowDefinitionStatus.Draft,
            IsPublished = false,
            RequireDistinctApprovers = change.RequireDistinctApprovers ?? true,
            ChangeReason = Trimmed(change.ChangeReason),
            CreatedBy = actor.UserId,
            LastEditedBy = actor.UserId
        };

        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync(ct);
        return new WorkflowDefinitionResult(definition);
    }

    public async Task<WorkflowDefinitionResult> UpdateDraftAsync(
        Guid id, WorkflowDefinitionChange change, WorkflowDefinitionActor actor, CancellationToken ct = default)
    {
        if (Refuse(actor) is { } refused)
            return refused;

        var definition = await db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (definition is null)
            return NotFound();
        if (definition.Status != WorkflowDefinitionStatus.Draft)
            return Invalid(NotDraftMessage);

        definition.Name = change.Name ?? definition.Name;
        definition.Description = change.Description ?? definition.Description;
        definition.DomainEntityType = change.DomainEntityType ?? definition.DomainEntityType;
        definition.ValidFrom = change.ValidFrom ?? definition.ValidFrom;
        definition.ValidTo = change.ValidTo ?? definition.ValidTo;
        definition.RequireDistinctApprovers = change.RequireDistinctApprovers ?? definition.RequireDistinctApprovers;
        definition.ChangeReason = Trimmed(change.ChangeReason) ?? definition.ChangeReason;
        if (change.Steps is not null)
            definition.StepsJson = JsonSerializer.Serialize(change.Steps);
        if (change.GuardrailRoles is not null)
            definition.GuardrailRolesJson = JsonSerializer.Serialize(change.GuardrailRoles);
        definition.LastEditedBy = actor.UserId;

        await db.SaveChangesAsync(ct);
        return new WorkflowDefinitionResult(definition);
    }

    /// <summary>Rule 1: only a draft is deleted; a version someone submitted or published stays on record.</summary>
    public async Task<WorkflowDefinitionResult> DeleteDraftAsync(Guid id, WorkflowDefinitionActor actor, CancellationToken ct = default)
    {
        if (Refuse(actor) is { } refused)
            return refused;

        var definition = await db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (definition is null)
            return NotFound();
        if (definition.Status != WorkflowDefinitionStatus.Draft)
            return Invalid("لا يُحذف إلا تعريف في حالة مسودة.");

        db.WorkflowDefinitions.Remove(definition);
        await db.SaveChangesAsync(ct);
        return new WorkflowDefinitionResult(definition);
    }

    /// <summary>Rule 2: a definition with errors stays a draft, and the author gets every error at once.</summary>
    public async Task<WorkflowDefinitionResult> SubmitForPublishAsync(Guid id, WorkflowDefinitionActor actor, CancellationToken ct = default)
    {
        if (Refuse(actor) is { } refused)
            return refused;

        var definition = await db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (definition is null)
            return NotFound();
        if (definition.Status != WorkflowDefinitionStatus.Draft)
            return Invalid("لا يُقدَّم إلا تعريف في حالة مسودة.");

        var errors = await ValidateAsync(definition, ct);
        if (errors.Count > 0)
            return new WorkflowDefinitionResult(definition, WorkflowDefinitionFailure.Invalid, errors);

        definition.Status = WorkflowDefinitionStatus.PendingPublish;
        definition.SubmittedBy = actor.UserId;
        definition.SubmittedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return new WorkflowDefinitionResult(definition);
    }

    /// <summary>
    /// Rules 3 and 5: a second person, the validator once more, and a plain warning when a policy binding pins another version.
    /// The last editor counts as an author: otherwise one person could rewrite a colleague's draft and publish it.
    /// </summary>
    public async Task<WorkflowDefinitionResult> PublishAsync(Guid id, WorkflowDefinitionActor actor, CancellationToken ct = default)
    {
        if (Refuse(actor) is { } refused)
            return refused;

        var definition = await db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (definition is null)
            return NotFound();
        if (definition.Status != WorkflowDefinitionStatus.PendingPublish)
            return Invalid(NotPendingMessage);
        if (actor.UserId == definition.CreatedBy || actor.UserId == definition.SubmittedBy || actor.UserId == definition.LastEditedBy)
            return new WorkflowDefinitionResult(null, WorkflowDefinitionFailure.Forbidden, [SecondPersonMessage]);

        var errors = await ValidateAsync(definition, ct);
        if (errors.Count > 0)
            return new WorkflowDefinitionResult(definition, WorkflowDefinitionFailure.Invalid, errors);

        definition.Status = WorkflowDefinitionStatus.Published;
        definition.IsPublished = true;
        definition.PublishedBy = actor.UserId;
        definition.PublishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return new WorkflowDefinitionResult(definition, Warnings: await BindingWarningsAsync(definition, ct));
    }

    public async Task<WorkflowDefinitionResult> ReturnToDraftAsync(
        Guid id, string? reason, WorkflowDefinitionActor actor, CancellationToken ct = default)
    {
        if (Refuse(actor) is { } refused)
            return refused;

        var definition = await db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (definition is null)
            return NotFound();
        if (definition.Status != WorkflowDefinitionStatus.PendingPublish)
            return Invalid(NotPendingMessage);

        definition.Status = WorkflowDefinitionStatus.Draft;
        definition.SubmittedBy = null;
        definition.SubmittedAt = null;
        definition.ChangeReason = Trimmed(reason) ?? definition.ChangeReason;
        await db.SaveChangesAsync(ct);
        return new WorkflowDefinitionResult(definition);
    }

    /// <summary>Rule 7: a reason, and never the last published version of a workflow the coordinators or a service start.</summary>
    public async Task<WorkflowDefinitionResult> RetireAsync(
        Guid id, string? reason, WorkflowDefinitionActor actor, CancellationToken ct = default)
    {
        if (Refuse(actor) is { } refused)
            return refused;

        var trimmed = Trimmed(reason);
        if (trimmed is null || trimmed.Length < MinRetireReasonLength)
            return Invalid(RetireReasonMessage);

        var definition = await db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (definition is null)
            return NotFound();
        if (definition.Status != WorkflowDefinitionStatus.Published)
            return Invalid(NotPublishedMessage);

        if (WorkflowDefinitionCatalog.KnownEntityTypes.Contains(definition.DomainEntityType))
        {
            var now = DateTime.UtcNow;
            var hasReplacement = await db.WorkflowDefinitions.AnyAsync(d =>
                d.Id != definition.Id
                && d.Code == definition.Code
                && d.IsPublished
                && (d.ValidFrom == null || d.ValidFrom <= now)
                && (d.ValidTo == null || d.ValidTo >= now), ct);
            if (!hasReplacement)
                return Invalid(LastPublishedMessage);
        }

        definition.Status = WorkflowDefinitionStatus.Retired;
        definition.IsPublished = false;
        definition.RetiredBy = actor.UserId;
        definition.RetiredAt = DateTime.UtcNow;
        definition.ChangeReason = trimmed;
        await db.SaveChangesAsync(ct);
        return new WorkflowDefinitionResult(definition);
    }

    /// <summary>
    /// The validator, plus what needs the database: a new version keeps the entity type of its code, or a code that starts by name
    /// could leave the limits of its own type by claiming a weaker one.
    /// </summary>
    private async Task<IReadOnlyList<string>> ValidateAsync(WorkflowDefinition definition, CancellationToken ct)
    {
        var errors = WorkflowDefinitionCatalog.Validate(definition).ToList();

        if (definition.ValidFrom is { } from && definition.ValidTo is { } to && to <= from)
            errors.Add(ValidityMessage);

        var otherType = await db.WorkflowDefinitions.AsNoTracking()
            .AnyAsync(d => d.Id != definition.Id && d.Code == definition.Code && d.DomainEntityType != definition.DomainEntityType, ct);
        if (otherType)
            errors.Add(EntityTypeChangedMessage);

        return errors;
    }

    private async Task<IReadOnlyList<string>> BindingWarningsAsync(WorkflowDefinition definition, CancellationToken ct)
    {
        var pinned = await (
                from binding in db.PolicyWorkflowBindings.AsNoTracking()
                join bound in db.WorkflowDefinitions.AsNoTracking() on binding.WorkflowDefinitionId equals bound.Id
                where binding.WorkflowCode == definition.Code && bound.Version != definition.Version
                select bound.Version)
            .Distinct()
            .ToListAsync(ct);

        return pinned.Order().Select(BindingWarning).ToList();
    }

    /// <summary>Rule 6, and every transition needs a person to name.</summary>
    private static WorkflowDefinitionResult? Refuse(WorkflowDefinitionActor actor)
    {
        if (actor.TenantId != TenantDefaults.DefaultTenantId)
            return new WorkflowDefinitionResult(null, WorkflowDefinitionFailure.Forbidden, [NationalOnlyMessage]);
        if (actor.UserId is null)
            return new WorkflowDefinitionResult(null, WorkflowDefinitionFailure.Forbidden, [ActorMessage]);
        return null;
    }

    private static WorkflowDefinitionResult NotFound() => new(null, WorkflowDefinitionFailure.NotFound);

    private static WorkflowDefinitionResult Invalid(string error) => new(null, WorkflowDefinitionFailure.Invalid, [error]);

    private static string? Trimmed(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        return trimmed.Length > MaxChangeReasonLength ? trimmed[..MaxChangeReasonLength] : trimmed;
    }
}

/// <summary>What the validator checks a stored definition against: the role catalog and the entity types the engine can finish.</summary>
public static class WorkflowDefinitionCatalog
{
    public static readonly IReadOnlySet<string> KnownRoles = new HashSet<string>(HrManagementRoles.All, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> KnownEntityTypes => WorkflowInstanceActionService.HandledEntityTypes;

    public static IReadOnlyList<string> Validate(WorkflowDefinition definition) =>
        WorkflowDefinitionValidator.Validate(
            new WorkflowDefinitionDraft(
                definition.Code,
                definition.Name,
                definition.DomainEntityType,
                Read<WorkflowStepSnapshot>(definition.StepsJson),
                Read<string>(definition.GuardrailRolesJson)),
            KnownRoles,
            KnownEntityTypes);

    private static IReadOnlyList<T> Read<T>(string json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<T>>(json) ?? [];
}
