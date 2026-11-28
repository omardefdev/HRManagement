using HR.Contracts.Application.Abstractions;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Application.Features.SeparationRecords;

/// <summary>
/// PLAN-60 (§4, G-SEP-01): HR completes a manual item by hand, with evidence (a reference of at least five characters, or a file), before
/// the execution. The person is the signed-in user: an empty one is refused. A System or PartyAck item is never completed by hand.
/// </summary>
public record CompleteManualStepCommand(
    Guid SeparationRecordId,
    SeparationStep Step,
    string? EvidenceRef,
    Guid? EvidenceFileId,
    string? Notes,
    Guid? ActorId,
    string? ActorName) : IRequest<(bool Success, string? Error)>;

/// <summary>PLAN-60 (§4): the first of the two requests of an exception: a manual item, with a reason of at least twenty characters.</summary>
public record RequestStepExceptionCommand(Guid SeparationRecordId, SeparationStep Step, string? Reason, Guid? RequesterId)
    : IRequest<(bool Success, string? Error)>;

/// <summary>PLAN-60 (§4): the second request, from another session: a holder of HrContractsApprove who is neither the requester nor the creator.</summary>
public record ApproveStepExceptionCommand(Guid SeparationRecordId, SeparationStep Step, Guid? ApproverId)
    : IRequest<(bool Success, string? Error)>;

/// <summary>The rules of a clearance item's manual completion and exception, in one place.</summary>
public static class SeparationChecklistRules
{
    public const int MinEvidenceLength = 5;
    public const int MinExceptionReasonLength = 20;

    public const string NoActorMessage = "تعذّر تحديد المستخدم الحالي — لم يُسجَّل شيء.";
    public const string NotEditableMessage = "لا تُعدَّل قائمة الإخلاء بعد التنفيذ أو الإلغاء أو الرفض.";
    public const string NotManualMessage = "هذا البند لا يُكمَل يدوياً: تكمله الخدمة نفسها أو إقرار الطرف المعني.";
    public const string PartyAckNotExceptedMessage = "بند الطرف لا يُستثنى من HR.Contracts: تعثّره يمر بالتدخل البشري في رحلة الفصل.";
    public const string DecisionNeverExceptedMessage = "لا يُستثنى إصدار القرار: لا تنفيذ دون قرار إداري مرقّم.";
    public const string EvidenceRequiredMessage = "الدليل مطلوب: مرجع من خمسة أحرف على الأقل أو ملف مرفق.";
    public const string ReasonTooShortMessage = "سبب الاستثناء إلزامي، عشرون حرفاً على الأقل.";
    public const string NoRequestMessage = "لا يوجد طلب استثناء مفتوح لهذا البند.";
    public const string SecondPersonMessage = "يعتمد الاستثناءَ شخصٌ ثانٍ: لا الطالب ولا منشئ سجل الفصل ولا مقدّمه للاعتماد.";
    public const string AlreadyDoneMessage = "البند مكتمل أو مستثنى مسبقاً.";
    public const string ItemMissingMessage = "البند غير موجود في قائمة الإخلاء.";

    /// <summary>Items change before the execution only, and not on a rejected or cancelled record.</summary>
    public static bool IsEditable(SeparationStatus status) =>
        status is SeparationStatus.Draft or SeparationStatus.PendingApproval or SeparationStatus.Approved;

    public static async Task<(SeparationRecord? Record, SeparationChecklist? Item, string? Error)> LoadAsync(
        IContractsRepository repository, Guid recordId, SeparationStep step, CancellationToken ct)
    {
        var record = await repository.GetSeparationRecordAsync(recordId, ct);
        if (record is null)
            return (null, null, null);
        if (!IsEditable(record.Status))
            return (record, null, NotEditableMessage);
        var item = await repository.SeparationChecklists.FirstOrDefaultAsync(c => c.SeparationRecordId == recordId && c.Step == step, ct);
        return item is null ? (record, null, ItemMissingMessage) : (record, item, null);
    }

    /// <summary>Only a manual item is excepted: never the decision, never a party's item.</summary>
    public static string? ExceptionRefusal(SeparationChecklist item) =>
        item.Step == SeparationStep.DecisionIssued ? DecisionNeverExceptedMessage
        : item.CompletionMode == SeparationStepCompletionMode.PartyAck ? PartyAckNotExceptedMessage
        : item.CompletionMode != SeparationStepCompletionMode.Manual ? NotManualMessage
        : null;
}

public class CompleteManualStepHandler(IContractsRepository repository, IEventPublisher eventPublisher)
    : IRequestHandler<CompleteManualStepCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(CompleteManualStepCommand request, CancellationToken cancellationToken)
    {
        if (request.ActorId is not Guid actor || actor == Guid.Empty)
            return (false, SeparationChecklistRules.NoActorMessage);

        var (record, item, error) = await SeparationChecklistRules.LoadAsync(repository, request.SeparationRecordId, request.Step, cancellationToken);
        if (record is null) return (false, null);
        if (error is not null) return (false, error);
        if (item!.CompletionMode != SeparationStepCompletionMode.Manual)
            return (false, SeparationChecklistRules.NotManualMessage);
        if (item.IsCompleted || item.IsExcepted)
            return (false, SeparationChecklistRules.AlreadyDoneMessage);

        var evidence = request.EvidenceRef?.Trim();
        if (request.EvidenceFileId is null && (evidence is null || evidence.Length < SeparationChecklistRules.MinEvidenceLength))
            return (false, SeparationChecklistRules.EvidenceRequiredMessage);

        item.IsCompleted = true;
        item.CompletedAt = DateTime.UtcNow;
        item.CompletedBy = actor;
        item.CompletedByName = string.IsNullOrWhiteSpace(request.ActorName) ? null : request.ActorName.Trim();
        item.EvidenceRef = evidence is { Length: > 0 } ? evidence[..Math.Min(evidence.Length, 256)] : $"file:{request.EvidenceFileId:D}";
        item.EvidenceFileId = request.EvidenceFileId;
        item.SourceSystem = "HRContracts";
        item.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

        await eventPublisher.PublishSeparationStepCompletedAsync(record.Id, record.EmployeeId, request.Step.ToString(), item.Notes, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}

public class RequestStepExceptionHandler(IContractsRepository repository) : IRequestHandler<RequestStepExceptionCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(RequestStepExceptionCommand request, CancellationToken cancellationToken)
    {
        if (request.RequesterId is not Guid requester || requester == Guid.Empty)
            return (false, SeparationChecklistRules.NoActorMessage);

        var (record, item, error) = await SeparationChecklistRules.LoadAsync(repository, request.SeparationRecordId, request.Step, cancellationToken);
        if (record is null) return (false, null);
        if (error is not null) return (false, error);
        if (SeparationChecklistRules.ExceptionRefusal(item!) is { } refusal)
            return (false, refusal);
        if (item!.IsCompleted || item.IsExcepted)
            return (false, SeparationChecklistRules.AlreadyDoneMessage);

        var reason = request.Reason?.Trim();
        if (reason is null || reason.Length < SeparationChecklistRules.MinExceptionReasonLength)
            return (false, SeparationChecklistRules.ReasonTooShortMessage);

        item.ExceptionRequestedBy = requester;
        item.ExceptionRequestedAt = DateTime.UtcNow;
        item.ExceptionReason = reason.Length <= 1000 ? reason : reason[..1000];
        item.ExceptionApprovedBy = null;
        item.ExceptionApprovedAt = null;
        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}

public class ApproveStepExceptionHandler(IContractsRepository repository) : IRequestHandler<ApproveStepExceptionCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(ApproveStepExceptionCommand request, CancellationToken cancellationToken)
    {
        if (request.ApproverId is not Guid approver || approver == Guid.Empty)
            return (false, SeparationChecklistRules.NoActorMessage);

        var (record, item, error) = await SeparationChecklistRules.LoadAsync(repository, request.SeparationRecordId, request.Step, cancellationToken);
        if (record is null) return (false, null);
        if (error is not null) return (false, error);
        if (SeparationChecklistRules.ExceptionRefusal(item!) is { } refusal)
            return (false, refusal);
        if (item!.IsCompleted || item.IsExcepted)
            return (false, SeparationChecklistRules.AlreadyDoneMessage);
        if (item.ExceptionRequestedBy is not Guid requester)
            return (false, SeparationChecklistRules.NoRequestMessage);
        if (approver == requester || approver == record.CreatedBy || approver == record.SubmittedBy)
            return (false, SeparationChecklistRules.SecondPersonMessage);

        item.ExceptionApprovedBy = approver;
        item.ExceptionApprovedAt = DateTime.UtcNow;
        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}
