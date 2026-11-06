using System.Text.Json;
using System.Text.RegularExpressions;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Workflow.External;

/// <summary>Who calls: read from the token and the two acting-user headers, never from the body.</summary>
public sealed record ExternalApprovalCaller(bool IsServiceAccount, string? ClientId, string? ActingUser, string? ActingUserName);

public enum ExternalApprovalOutcome
{
    Started,
    Existing,
    Withdrawn,
    Forbidden,
    Invalid,
    Conflict,
    DefinitionMissing,
    NotFound
}

public sealed record ExternalApprovalResult(ExternalApprovalOutcome Outcome, WorkflowInstance? Instance = null, string? Error = null);

/// <summary>
/// PLAN-43 (G-WF-05): the only way to start a type of <see cref="ExternalApprovalTypes"/>. The owning service account starts and
/// withdraws; the requester is the human it vouches for through X-Acting-User, and is barred from every step. PLAN-46 builds the
/// general contract on this core.
/// </summary>
public sealed partial class ExternalApprovalService(HRCoreDbContext db, IWorkflowEngine engine)
{
    public const string NotOwnerMessage = "هذه الخدمة لا تملك بدء هذا النوع.";
    public const string WorkflowCodeMessage = "رمز المسار غير مسموح لهذا النوع.";
    public const string RequesterMessage = "تعذّر تحديد طالب الإجراء.";
    public const string IdempotencyKeyMessage = "مفتاح عدم التكرار مطلوب (200 حرف على الأكثر).";
    public const string KeyReusedMessage = "مفتاح عدم التكرار مستعمل لكيان آخر.";
    public const string ActiveInstanceMessage = "يوجد مسار جارٍ لهذا الكيان.";
    public const string DisplayTitleMessage = "عنوان العرض مطلوب (300 حرف على الأكثر) ولا يتضمن رقماً طويلاً.";
    public const string DetailsUrlMessage = "رابط التفاصيل يجب أن يكون مساراً نسبياً في الخدمة المالكة.";
    public const string FactsMessage = "الحقائق 20 بنداً على الأكثر، والمفتاح 50 حرفاً والقيمة 200 حرف.";
    public const string CancelReasonMessage = "سبب السحب مطلوب.";
    public const string FinishedMessage = "لا يمكن سحب مسار منتهٍ.";

    public const int MaxIdempotencyKeyLength = 200;
    public const int MaxDisplayTitleLength = 300;
    public const int MaxDetailsUrlLength = 500;
    public const int MaxFacts = 20;
    public const int MaxFactKeyLength = 50;
    public const int MaxFactValueLength = 200;
    public const int MaxRequesterNameLength = 200;

    public async Task<ExternalApprovalResult> StartAsync(
        WorkflowApprovalStartRequest request, ExternalApprovalCaller caller, CancellationToken ct = default)
    {
        // Rule 1: the owner of the type, and an unregistered type has no owner.
        var type = ExternalApprovalTypes.Find(request.EntityType);
        if (type is null || !IsOwner(type, caller))
            return new ExternalApprovalResult(ExternalApprovalOutcome.Forbidden, Error: NotOwnerMessage);

        // Rule 2.
        var workflowCode = string.IsNullOrWhiteSpace(request.WorkflowCode) ? type.DefaultWorkflowCode : request.WorkflowCode;
        if (!type.AllowedWorkflowCodes.Contains(workflowCode, StringComparer.OrdinalIgnoreCase))
            return Invalid(WorkflowCodeMessage);

        // Rule 3: the requester comes from the owning service's header, and never from the body.
        if (!Guid.TryParse(caller.ActingUser, out var requesterId) || requesterId == Guid.Empty)
            return Invalid(RequesterMessage);

        // Rule 4: the key first and in every state, so a retry after a timeout never makes a second instance.
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > MaxIdempotencyKeyLength)
            return Invalid(IdempotencyKeyMessage);

        var tenantId = db.CurrentTenantId ?? TenantDefaults.DefaultTenantId;
        var existing = await db.WorkflowInstances.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.SourceClientId == caller.ClientId && i.IdempotencyKey == request.IdempotencyKey)
            .OrderByDescending(i => i.StartedAt)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            return existing.EntityId == request.EntityId
                && string.Equals(existing.EntityType, type.EntityType, StringComparison.OrdinalIgnoreCase)
                    ? new ExternalApprovalResult(ExternalApprovalOutcome.Existing, existing)
                    : new ExternalApprovalResult(ExternalApprovalOutcome.Conflict, existing, KeyReusedMessage);
        }

        var active = await db.WorkflowInstances.AsNoTracking()
            .Where(i => i.TenantId == tenantId
                && i.EntityType == type.EntityType
                && i.EntityId == request.EntityId
                && i.Status == WorkflowStatus.InProgress)
            .FirstOrDefaultAsync(ct);
        if (active is not null)
            return new ExternalApprovalResult(ExternalApprovalOutcome.Conflict, active, ActiveInstanceMessage);

        // Rule 5.
        if (ValidateContent(request, type) is { } contentError)
            return Invalid(contentError);

        // Rule 6: the latest valid published version of the code; a policy binding has no say here.
        var now = DateTime.UtcNow;
        var definition = await db.WorkflowDefinitions
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PolicyResolution)
            .AsNoTracking()
            .Where(d => d.Code == workflowCode
                && d.DomainEntityType == type.EntityType
                && d.IsPublished
                && (d.ValidFrom == null || d.ValidFrom <= now)
                && (d.ValidTo == null || d.ValidTo >= now)
                && (d.TenantId == tenantId || d.TenantId == TenantDefaults.DefaultTenantId))
            .OrderByDescending(d => d.TenantId == tenantId)
            .ThenByDescending(d => d.Version)
            .FirstOrDefaultAsync(ct);
        if (definition is null)
            return new ExternalApprovalResult(ExternalApprovalOutcome.DefinitionMissing, Error: ApprovalStartCoordinator.MissingDefinitionMessage);

        // Rule 7.
        var requesterName = Bounded(caller.ActingUserName, MaxRequesterNameLength);
        var contextJson = request.Facts is null ? null : JsonSerializer.Serialize(request.Facts);
        var started = engine.Start(
            WorkflowDefinitionMapper.ToSnapshot(definition), type.EntityType, request.EntityId, requesterId, requesterName, contextJson);
        if (!started.Success)
            return new ExternalApprovalResult(ExternalApprovalOutcome.DefinitionMissing, Error: ApprovalStartCoordinator.MissingDefinitionMessage);

        var instance = new WorkflowInstance
        {
            TenantId = tenantId,
            DefinitionId = definition.Id,
            DefinitionVersion = definition.Version,
            EntityType = type.EntityType,
            EntityId = request.EntityId,
            CurrentStepLevel = started.NewStepLevel,
            Status = started.NewStatus,
            ContextJson = contextJson,
            InitiatedBy = requesterId,
            InitiatedByName = requesterName,
            StartedAt = now,
            CompletedAt = started.NewStatus == WorkflowStatus.Completed ? now : null,
            RequestedByUserId = requesterId,
            RequestedByName = requesterName,
            SourceClientId = caller.ClientId,
            IdempotencyKey = request.IdempotencyKey,
            ScopeOrgUnitId = request.OrgUnitId,
            SubjectEmployeeId = request.SubjectEmployeeId,
            ExcludedApproverUserIdsJson = JsonSerializer.Serialize(request.ExcludedApproverUserIds ?? []),
            DisplayTitle = request.DisplayTitle,
            DetailsUrl = request.DetailsUrl
        };

        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync(ct);
        return new ExternalApprovalResult(ExternalApprovalOutcome.Started, instance);
    }

    /// <summary>Rule 8: the owner withdraws a running instance. PLAN-46 adds an outcome event for it; none is published here.</summary>
    public async Task<ExternalApprovalResult> CancelAsync(
        Guid instanceId, ExternalApprovalCaller caller, string? reason, CancellationToken ct = default)
    {
        var instance = await db.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == instanceId, ct);
        if (instance is null)
            return new ExternalApprovalResult(ExternalApprovalOutcome.NotFound);

        var type = ExternalApprovalTypes.Find(instance.EntityType);
        if (type is null || !IsOwner(type, caller))
            return new ExternalApprovalResult(ExternalApprovalOutcome.Forbidden, Error: NotOwnerMessage);

        if (string.IsNullOrWhiteSpace(reason))
            return Invalid(CancelReasonMessage);

        if (instance.Status != WorkflowStatus.InProgress)
            return new ExternalApprovalResult(ExternalApprovalOutcome.Conflict, instance, FinishedMessage);

        var actorId = Guid.TryParse(caller.ActingUser, out var actor) ? actor : (Guid?)null;
        var withdrawn = engine.Withdraw(
            WorkflowDefinitionMapper.ToState(instance), actorId, Bounded(caller.ActingUserName, MaxRequesterNameLength), reason.Trim());
        if (!withdrawn.Success)
            return new ExternalApprovalResult(ExternalApprovalOutcome.Conflict, instance, FinishedMessage);

        var history = JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(instance.StepHistoryJson) ?? [];
        history.Add(withdrawn.HistoryEntry!);
        instance.StepHistoryJson = JsonSerializer.Serialize(history);
        instance.Status = withdrawn.NewStatus;
        instance.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return new ExternalApprovalResult(ExternalApprovalOutcome.Withdrawn, instance);
    }

    private static bool IsOwner(ExternalApprovalType type, ExternalApprovalCaller caller) =>
        caller.IsServiceAccount && string.Equals(caller.ClientId, type.OwnerClientId, StringComparison.Ordinal);

    private static string? ValidateContent(WorkflowApprovalStartRequest request, ExternalApprovalType type)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayTitle)
            || request.DisplayTitle.Length > MaxDisplayTitleLength
            || LongNumber().IsMatch(request.DisplayTitle))
            return DisplayTitleMessage;

        if (!IsDetailsUrl(request.DetailsUrl, type.DetailsUrlPrefix))
            return DetailsUrlMessage;

        if (request.Facts is { } facts
            && (facts.Count > MaxFacts
                || facts.Any(f => string.IsNullOrWhiteSpace(f.Key)
                    || f.Key.Length > MaxFactKeyLength
                    || (f.Value?.Length ?? 0) > MaxFactValueLength)))
            return FactsMessage;

        return null;
    }

    /// <summary>A path inside the owner's UI: no scheme, no other host, no way back out of the prefix.</summary>
    private static bool IsDetailsUrl(string? url, string prefix) =>
        !string.IsNullOrWhiteSpace(url)
        && url.Length <= MaxDetailsUrlLength
        && url.StartsWith(prefix, StringComparison.Ordinal)
        && !url.Contains("..", StringComparison.Ordinal)
        && !url.Contains("//", StringComparison.Ordinal)
        && !url.Contains('\\')
        && !url.Any(char.IsControl);

    private static string? Bounded(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    private static ExternalApprovalResult Invalid(string error) => new(ExternalApprovalOutcome.Invalid, Error: error);

    /// <summary>Eight digits or more: a national number must not reach a title that inboxes show.</summary>
    [GeneratedRegex(@"\d{8,}")]
    private static partial Regex LongNumber();
}
