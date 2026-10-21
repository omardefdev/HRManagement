using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Models;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Application.Services;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.SharedKernel.Legal;
using MediatR;
using Microsoft.EntityFrameworkCore;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Contracts.Application.Features.SeparationRecords;

public record GetSeparationRecordsQuery : IRequest<IReadOnlyList<SeparationRecord>>;
public record GetSeparationRecordByIdQuery(Guid Id) : IRequest<SeparationRecord?>;
public record CreateSeparationRecordCommand(SeparationRecordDto Model) : IRequest<Guid>;
public record UpdateSeparationRecordCommand(Guid Id, SeparationRecordDto Model) : IRequest<bool>;
public record DeleteSeparationRecordCommand(Guid Id) : IRequest<bool>;
public record PublishSeparationCommand(Guid Id) : IRequest<(bool Success, string? Error)>;
public record CancelSeparationCommand(Guid Id, string? Reason = null) : IRequest<(bool Success, string? Error)>;
public record CompleteSeparationChecklistStepCommand(
    Guid SeparationRecordId,
    HR.Contracts.Domain.Enums.SeparationStep Step,
    string? Notes,
    Guid? CompletedBy = null,
    string? EvidenceRef = null,
    string? SourceSystem = null) : IRequest<(bool Success, string? Error)>;
public record OverrideSeparationChecklistStepCommand(
    Guid SeparationRecordId,
    HR.Contracts.Domain.Enums.SeparationStep Step,
    string Reason,
    Guid OverriddenBy,
    Guid ApprovedBy) : IRequest<(bool Success, string? Error)>;

public class GetSeparationRecordsHandler(IContractsRepository repository) : IRequestHandler<GetSeparationRecordsQuery, IReadOnlyList<SeparationRecord>>
{
    public async Task<IReadOnlyList<SeparationRecord>> Handle(GetSeparationRecordsQuery request, CancellationToken cancellationToken) =>
        await repository.SeparationRecords
            .Include(s => s.Contract)
            .AsNoTracking()
            .OrderByDescending(s => s.EffectiveDate)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync(cancellationToken);
}

public class GetSeparationRecordByIdHandler(IContractsRepository repository) : IRequestHandler<GetSeparationRecordByIdQuery, SeparationRecord?>
{
    public async Task<SeparationRecord?> Handle(GetSeparationRecordByIdQuery request, CancellationToken cancellationToken) =>
        await repository.SeparationRecords
            .Include(s => s.Contract)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);
}

public class CreateSeparationRecordHandler(
    IContractsRepository repository,
    ICodeGenerationClient codeClient) : IRequestHandler<CreateSeparationRecordCommand, Guid>
{
    public async Task<Guid> Handle(CreateSeparationRecordCommand request, CancellationToken cancellationToken)
    {
        var vm = request.Model;
        var entity = new SeparationRecord
        {
            EmployeeId = vm.EmployeeId,
            ContractId = vm.ContractId,
            SeparationType = vm.SeparationType,
            EffectiveDate = vm.EffectiveDate,
            Reason = string.IsNullOrWhiteSpace(vm.Reason) ? null : vm.Reason.Trim(),
            ApprovedBy = vm.ApprovedBy,
            Documents = string.IsNullOrWhiteSpace(vm.Documents) ? null : vm.Documents.Trim(),
            SeparationEventPublished = false
        };
        await repository.AddAsync(entity, cancellationToken);
        var transaction = new ServiceTransaction
        {
            EmployeeId = vm.EmployeeId,
            TransactionType = ServiceTransactionType.Separation,
            Notes = "تسجيل إنهاء خدمة"
        };
        await ServiceTransactionReferenceSupport.ApplyReferenceAsync(transaction, codeClient, cancellationToken);
        await repository.AddAsync(transaction, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        await SeparationRecordSupport.SeedChecklistAsync(repository, entity.Id, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }
}

public class UpdateSeparationRecordHandler(IContractsRepository repository) : IRequestHandler<UpdateSeparationRecordCommand, bool>
{
    public async Task<bool> Handle(UpdateSeparationRecordCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetSeparationRecordAsync(request.Id, cancellationToken);
        if (entity is null) return false;
        if (entity.SeparationEventPublished) return false;
        if (entity.IsCancelled) return false;
        var vm = request.Model;
        entity.EmployeeId = vm.EmployeeId;
        entity.ContractId = vm.ContractId;
        entity.SeparationType = vm.SeparationType;
        entity.EffectiveDate = vm.EffectiveDate;
        entity.Reason = string.IsNullOrWhiteSpace(vm.Reason) ? null : vm.Reason.Trim();
        entity.ApprovedBy = vm.ApprovedBy;
        entity.Documents = string.IsNullOrWhiteSpace(vm.Documents) ? null : vm.Documents.Trim();
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public class DeleteSeparationRecordHandler(IContractsRepository repository, IEventPublisher eventPublisher)
    : IRequestHandler<DeleteSeparationRecordCommand, bool>
{
    public async Task<bool> Handle(DeleteSeparationRecordCommand request, CancellationToken cancellationToken)
    {
        // FUN-002: never hard-delete — route through soft-cancel.
        var (ok, _) = await new CancelSeparationHandler(repository, eventPublisher)
            .Handle(new CancelSeparationCommand(request.Id, "حذف إداري → soft-cancel"), cancellationToken);
        return ok;
    }
}

public class PublishSeparationHandler(
    IContractsRepository repository,
    IEventPublisher eventPublisher,
    IEmployeeLifecycleGate lifecycleGate,
    IHrCoreActiveRegimeClient activeRegimeClient) : IRequestHandler<PublishSeparationCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(PublishSeparationCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetSeparationRecordAsync(request.Id, cancellationToken);
        if (entity is null) return (false, null);
        if (entity.IsCancelled) return (false, "سجل الفصل ملغى — لا يمكن النشر.");
        if (entity.SeparationEventPublished) return (false, "تم إشعار الأنظمة المرتبطة مسبقاً");

        if (!SeparationWorkflowRules.IsEffectiveDateReached(entity))
            return (false, $"تاريخ النفاذ ({entity.EffectiveDate:yyyy-MM-dd}) لم يحن بعد — لا يمكن بدء رحلة الفصل.");

        var (holdOk, holdError) = await lifecycleGate.AssertCanPublishSeparationAsync(entity.EmployeeId, cancellationToken);
        if (!holdOk)
            return (false, holdError);

        await SeparationRecordSupport.SeedChecklistAsync(repository, entity.Id, cancellationToken);

        var checklists = await repository.SeparationChecklists
            .Where(c => c.SeparationRecordId == entity.Id)
            .ToListAsync(cancellationToken);

        // DOM-001 / INT-003: do NOT auto-complete ContractTerminated (or any step).
        // Contract termination happens when the operator completes that checklist step with evidence.

        if (!SeparationWorkflowRules.CanComplete(entity, checklists))
        {
            var incompleteSteps = SeparationWorkflowRules.GetIncompleteRequiredSteps(checklists)
                .Select(step => step.Step.ToString())
                .ToArray();
            var summary = incompleteSteps.Length == 0
                ? "هناك خطوات إلزامية لم تُكمل بعد."
                : $"هناك خطوات إلزامية لم تُكمل بعد: {string.Join(", ", incompleteSteps)}";
            return (false, $"لا يمكن نشر الفصل: {summary}");
        }

        if (string.IsNullOrWhiteSpace(entity.SourceLegalRegimeId))
        {
            var (sourceRegime, payrollCode, regimeError) =
                await SeparationRegimeValidationService.SnapshotAndValidateAsync(
                    entity.EmployeeId,
                    entity.SeparationType.ToString(),
                    activeRegimeClient,
                    cancellationToken);

            if (regimeError is not null)
                return (false, regimeError);

            entity.SourceLegalRegimeId = sourceRegime;
            entity.PayrollRegimeCode = payrollCode;
            await repository.SaveChangesAsync(cancellationToken);
        }

        var remainingLeaveDays = await SeparationRecordSupport.ComputeAnnualLeaveRemainingAsync(
            repository, entity.EmployeeId, entity.EffectiveDate, cancellationToken);

        await eventPublisher.StartSeparationSagaAsync(
            entity.Id,
            entity.EmployeeId,
            entity.SeparationType.ToString(),
            entity.EffectiveDate,
            cancellationToken,
            contractId: entity.ContractId,
            remainingLeaveDays: remainingLeaveDays,
            sourceLegalRegimeId: entity.SourceLegalRegimeId,
            payrollRegimeCode: entity.PayrollRegimeCode);
        entity.SeparationEventPublished = true;

        // The transaction advances to Executing because the saga has started; it must not be
        // archived here. Archiving at publication time declared the separation finished before a
        // single party had acknowledged, so a party failing afterwards left an archived record of
        // work that never completed. SeparationSagaFinalizedConsumer archives it once every party
        // has succeeded.
        var tx = await repository.ServiceTransactions
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(x => x.EmployeeId == entity.EmployeeId && x.TransactionType == ServiceTransactionType.Separation, cancellationToken);
        if (tx is not null)
        {
            if (tx.State == ServiceTransactionState.Draft) tx.MoveTo(ServiceTransactionState.Submitted, null, null);
            if (tx.State == ServiceTransactionState.Submitted) tx.MoveTo(ServiceTransactionState.UnderReview, null, null);
            if (tx.State == ServiceTransactionState.UnderReview) tx.MoveTo(ServiceTransactionState.InProcess, null, null);
            if (tx.State == ServiceTransactionState.InProcess) tx.MoveTo(ServiceTransactionState.PendingApproval, null, null);
            if (tx.State == ServiceTransactionState.PendingApproval) tx.MoveTo(ServiceTransactionState.Approved, null, null);
            if (tx.State == ServiceTransactionState.Approved) tx.MoveTo(ServiceTransactionState.Executing, null, null);
        }

        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}

public class CompleteSeparationChecklistStepHandler(IContractsRepository repository, IEventPublisher eventPublisher)
    : IRequestHandler<CompleteSeparationChecklistStepCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(
        CompleteSeparationChecklistStepCommand request,
        CancellationToken cancellationToken)
    {
        var record = await repository.GetSeparationRecordAsync(request.SeparationRecordId, cancellationToken);
        if (record is null) return (false, null);

        var step = await repository.SeparationChecklists
            .FirstOrDefaultAsync(c => c.SeparationRecordId == request.SeparationRecordId && c.Step == request.Step, cancellationToken);
        if (step is null) return (false, "خطوة غير موجودة");
        if (step.IsCompleted) return (true, null);
        if (record.SeparationEventPublished)
            return (false, "لا يمكن تعديل قائمة الفصل بعد نشر حدث الإنهاء.");
        if (record.IsCancelled)
            return (false, "سجل الفصل ملغى.");

        if (string.IsNullOrWhiteSpace(request.EvidenceRef) && string.IsNullOrWhiteSpace(request.Notes))
            return (false, "الدليل (Evidence) مطلوب لإكمال خطوة قائمة الفصل.");

        if (request.CompletedBy == Guid.Empty)
            return (false, "معرّف المنفّذ مطلوب لإكمال خطوة قائمة الفصل.");

        // DOM-001: terminating the linked contract is an explicit ContractTerminated step action.
        if (request.Step == SeparationStep.ContractTerminated)
            await SeparationRecordSupport.TerminateLinkedContractAsync(repository, record, cancellationToken);

        step.IsCompleted = true;
        step.CompletedAt = DateTime.UtcNow;
        step.CompletedBy = request.CompletedBy;
        step.EvidenceRef = string.IsNullOrWhiteSpace(request.EvidenceRef) ? request.Notes?.Trim() : request.EvidenceRef?.Trim();
        step.SourceSystem = string.IsNullOrWhiteSpace(request.SourceSystem) ? "HRContracts" : request.SourceSystem.Trim();
        step.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        await repository.SaveChangesAsync(cancellationToken);

        await eventPublisher.PublishSeparationStepCompletedAsync(
            record.Id,
            record.EmployeeId,
            request.Step.ToString(),
            request.Notes,
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return (true, null);
    }
}

public class OverrideSeparationChecklistStepHandler(IContractsRepository repository)
    : IRequestHandler<OverrideSeparationChecklistStepCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(
        OverrideSeparationChecklistStepCommand request,
        CancellationToken cancellationToken)
    {
        if (request.OverriddenBy == request.ApprovedBy)
            return (false, "يتطلب الاستثناء موافقة مزدوجة من شخصين مختلفين.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return (false, "سبب الاستثناء إلزامي.");

        var record = await repository.GetSeparationRecordAsync(request.SeparationRecordId, cancellationToken);
        if (record is null) return (false, null);
        if (record.SeparationEventPublished)
            return (false, "لا يمكن تعديل قائمة الفصل بعد نشر حدث الإنهاء.");

        var step = await repository.SeparationChecklists
            .FirstOrDefaultAsync(c => c.SeparationRecordId == request.SeparationRecordId && c.Step == request.Step, cancellationToken);
        if (step is null) return (false, "خطوة غير موجودة");
        if (step.IsCompleted) return (true, null);

        var now = DateTime.UtcNow;
        var reason = request.Reason.Trim();
        step.IsCompleted = true;
        step.CompletedAt = now;
        step.CompletedBy = request.OverriddenBy;
        step.EvidenceRef = $"OVERRIDE:{reason}";
        step.SourceSystem = "Override";
        step.Notes = $"Override approved by {request.ApprovedBy}: {reason}";

        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}

public class CancelSeparationHandler(IContractsRepository repository, IEventPublisher eventPublisher)
    : IRequestHandler<CancelSeparationCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(CancelSeparationCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetSeparationRecordAsync(request.Id, cancellationToken);
        if (entity is null) return (false, null);

        if (!SeparationWorkflowRules.CanCancel(entity))
            return (false, "لا يمكن إلغاء سجل الفصل بعد النفاذ أو بعد اكتمال لا رجعة فيه.");

        var wasPublished = entity.SeparationEventPublished;
        SeparationWorkflowRules.SoftCancel(entity, request.Reason);

        if (wasPublished)
        {
            await eventPublisher.CancelSeparationSagaAsync(
                entity.Id,
                entity.CancellationReason ?? "إلغاء قبل النفاذ",
                cancellationToken);
        }

        var tx = await repository.ServiceTransactions
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(x => x.EmployeeId == entity.EmployeeId
                && x.TransactionType == ServiceTransactionType.Separation, cancellationToken);
        if (tx is not null && tx.State != ServiceTransactionState.Cancelled)
            tx.MoveTo(ServiceTransactionState.Cancelled, null, "إلغاء سجل الفصل");

        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}
