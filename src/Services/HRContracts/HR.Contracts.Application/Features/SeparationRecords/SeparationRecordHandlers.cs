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

/// <summary>Separation list, newest first, at most MaxPageSize rows. <c>EmployeeIds</c> (QA-034): when set, only these
/// employees' rows — applied before the page limit, so a branch user's list is not cut to the tenant's newest rows.</summary>
public record GetSeparationRecordsQuery(IReadOnlyCollection<Guid>? EmployeeIds = null) : IRequest<IReadOnlyList<SeparationRecord>>;
public record GetSeparationRecordByIdQuery(Guid Id) : IRequest<SeparationRecord?>;
public record CreateSeparationRecordCommand(SeparationRecordDto Model, string? TransactionNote = null) : IRequest<SeparationRecordCreateResult>;
public record UpdateSeparationRecordCommand(Guid Id, SeparationRecordDto Model) : IRequest<(bool Success, string? Error)>;
/// <summary>PLAN-60 (§5): the execution replaces the publication — only on an approved record, with its decision and items, on Damascus's date.</summary>
public record ExecuteSeparationCommand(Guid Id, Guid? ActorUserId) : IRequest<(bool Success, string? Error)>;

/// <summary>PLAN-60 (§5): a cancellation needs a reason, and runs from Draft, PendingApproval or Approved only. A record is never deleted.</summary>
public record CancelSeparationCommand(Guid Id, string? Reason, Guid? ActorUserId) : IRequest<(bool Success, string? Error)>;

public class GetSeparationRecordsHandler(IContractsRepository repository) : IRequestHandler<GetSeparationRecordsQuery, IReadOnlyList<SeparationRecord>>
{
    public async Task<IReadOnlyList<SeparationRecord>> Handle(GetSeparationRecordsQuery request, CancellationToken cancellationToken)
    {
        var query = repository.SeparationRecords
            .Include(s => s.Contract)
            .AsNoTracking();
        if (request.EmployeeIds is { } employeeIds)
            query = query.Where(s => employeeIds.Contains(s.EmployeeId));
        return await query
            .OrderByDescending(s => s.EffectiveDate)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync(cancellationToken);
    }
}

public class GetSeparationRecordByIdHandler(IContractsRepository repository) : IRequestHandler<GetSeparationRecordByIdQuery, SeparationRecord?>
{
    public async Task<SeparationRecord?> Handle(GetSeparationRecordByIdQuery request, CancellationToken cancellationToken) =>
        await repository.SeparationRecords
            .Include(s => s.Contract)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);
}

public class CreateSeparationRecordHandler(SeparationRecordFactory factory)
    : IRequestHandler<CreateSeparationRecordCommand, SeparationRecordCreateResult>
{
    /// <summary>PLAN-60 (§6): the form's entry goes through the one factory, like the wizard's and the inbound requests'.</summary>
    public Task<SeparationRecordCreateResult> Handle(CreateSeparationRecordCommand request, CancellationToken cancellationToken)
    {
        var vm = request.Model;
        return factory.CreateAsync(
            new SeparationRecordDraft(vm.EmployeeId, vm.ContractId, vm.SeparationType, vm.EffectiveDate, vm.Reason, vm.Documents,
                request.TransactionNote ?? "تسجيل إنهاء خدمة"),
            cancellationToken);
    }
}

public class UpdateSeparationRecordHandler(IContractsRepository repository, SeparationRecordFactory factory)
    : IRequestHandler<UpdateSeparationRecordCommand, (bool Success, string? Error)>
{
    public const string NotDraftMessage = "يُعدَّل سجل الفصل وهو مسودة فقط.";
    public const string SubjectLockedMessage = "لا يتغير الموظف ولا العقد بعد إكمال أي بند من قائمة الإخلاء أو استثنائه.";

    /// <summary>
    /// PLAN-60 (§6): the substance is locked. Edits only in Draft; once an item is completed or excepted, the employee and the contract no
    /// longer change; a changed employee or contract is checked as a creation is. The status, the published flag and the old approver are
    /// never taken from a form.
    /// </summary>
    public async Task<(bool Success, string? Error)> Handle(UpdateSeparationRecordCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetSeparationRecordAsync(request.Id, cancellationToken);
        if (entity is null) return (false, null);
        if (entity.Status != SeparationStatus.Draft) return (false, NotDraftMessage);

        var vm = request.Model;
        var subjectChanged = vm.EmployeeId != entity.EmployeeId || vm.ContractId != entity.ContractId;
        if (subjectChanged)
        {
            var touched = await repository.SeparationChecklists.AnyAsync(
                c => c.SeparationRecordId == entity.Id && (c.IsCompleted || c.ExceptionApprovedAt != null), cancellationToken);
            if (touched)
                return (false, SubjectLockedMessage);

            var check = await factory.CheckSubjectAsync(vm.EmployeeId, vm.ContractId, entity.Id, cancellationToken);
            if (!check.Success)
                return (false, check.Error);
            entity.EmployeeId = vm.EmployeeId;
            entity.ContractId = check.ContractId;

            var transaction = await repository.ServiceTransactions
                .FirstOrDefaultAsync(t => t.SeparationRecordId == entity.Id, cancellationToken);
            if (transaction is not null)
                transaction.EmployeeId = vm.EmployeeId;
        }

        entity.SeparationType = vm.SeparationType;
        entity.EffectiveDate = vm.EffectiveDate;
        entity.Reason = string.IsNullOrWhiteSpace(vm.Reason) ? null : vm.Reason.Trim();
        entity.Documents = string.IsNullOrWhiteSpace(vm.Documents) ? null : vm.Documents.Trim();
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (SeparationRecordFactory.IsOpenRecordViolation(ex))
        {
            return (false, SeparationRecordFactory.OpenRecordMessage);
        }

        return (true, null);
    }
}

public class ExecuteSeparationHandler(
    IContractsRepository repository,
    IEventPublisher eventPublisher,
    IEmployeeLifecycleGate lifecycleGate,
    IHrCoreActiveRegimeClient activeRegimeClient,
    SeparationClock clock) : IRequestHandler<ExecuteSeparationCommand, (bool Success, string? Error)>
{
    public const string NotApprovedMessage = "لا يُنفَّذ الفصل قبل اعتماده في محرك سير العمل.";
    public const string AlreadyExecutedMessage = "نُفِّذ هذا الفصل مسبقاً — بدأت رحلة الأطراف.";
    public const string NoDecisionMessage = "لا يُنفَّذ الفصل دون قرار إداري مرقّم: أصدر القرار أولاً.";
    public const string NoActorMessage = "تعذّر تحديد المستخدم الحالي — لم يُنفَّذ الفصل.";

    public static string NotYetMessage(DateOnly effective, DateOnly today) =>
        $"تاريخ النفاذ ({effective:yyyy-MM-dd}) لم يحن بعد بتوقيت دمشق (اليوم {today:yyyy-MM-dd}) — لا يُنفَّذ الفصل قبله.";

    /// <summary>
    /// PLAN-60 (§5, D-SEP-6): the order of the checks is the plan's — the status, the decision, the pre-execution items, the effective
    /// date in Damascus, the legal hold (PLAN-42, fail-closed), the service regime. Then one save: the contract ended at the effective date,
    /// its item (System), <c>Executing</c> with its actor, the transaction moved, and <c>IStartSeparationSaga</c> in the outbox — all or
    /// nothing. No other path starts the saga.
    /// </summary>
    public async Task<(bool Success, string? Error)> Handle(ExecuteSeparationCommand request, CancellationToken cancellationToken)
    {
        if (request.ActorUserId is not Guid actor || actor == Guid.Empty)
            return (false, NoActorMessage);

        var entity = await repository.GetSeparationRecordAsync(request.Id, cancellationToken);
        if (entity is null) return (false, null);
        if (SeparationWorkflowRules.IsExecutedOrLater(entity.Status))
            return (false, AlreadyExecutedMessage);
        if (entity.Status != SeparationStatus.Approved)
            return (false, NotApprovedMessage);

        if (entity.DecisionId is not Guid decisionId
            || !await repository.AdministrativeDecisions.AnyAsync(d => d.Id == decisionId, cancellationToken))
            return (false, NoDecisionMessage);

        var checklists = await repository.SeparationChecklists
            .Where(c => c.SeparationRecordId == entity.Id)
            .ToListAsync(cancellationToken);
        var open = SeparationWorkflowRules.GetOpenPreExecutionSteps(checklists);
        if (open.Count > 0)
            return (false, $"لا يمكن تنفيذ الفصل: بنود لم تُكمل بعد: {string.Join("، ", open.Select(o => SeparationWorkflowRules.DisplayName(o.Step)))}");

        var today = clock.Today;
        if (!SeparationWorkflowRules.IsEffectiveDateReached(entity, today))
            return (false, NotYetMessage(entity.EffectiveDate, today));

        var (holdOk, holdError) = await lifecycleGate.AssertCanPublishSeparationAsync(entity.EmployeeId, cancellationToken);
        if (!holdOk)
            return (false, holdError);

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
        }

        // The contract ends at the effective date, and its item is the service's own (System).
        var (contract, contractError) = await SeparationRecordSupport.TerminateLinkedContractAsync(repository, entity, cancellationToken);
        if (contractError is not null)
            return (false, contractError);
        var contractItem = checklists.FirstOrDefault(c => c.Step == SeparationStep.ContractTerminated);
        if (contractItem is not null && !contractItem.IsCompleted)
        {
            contractItem.IsCompleted = true;
            contractItem.CompletedAt = clock.UtcNow;
            contractItem.CompletedBy = actor;
            contractItem.SourceSystem = "HRContracts";
            contractItem.EvidenceRef = contract is null
                ? "لا عقد مرتبط بسجل الفصل"
                : $"contract:{contract.Id:D}:end:{entity.EffectiveDate:yyyy-MM-dd}";
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

        // The record's transition moves its own transaction, with the actor (P-4).
        await SeparationStatusSync.TransitionAsync(
            repository, entity, SeparationStatus.Executing, actor, "تنفيذ الفصل", cancellationToken);

        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}

public class CancelSeparationHandler(IContractsRepository repository, SeparationApprovalService approvals)
    : IRequestHandler<CancelSeparationCommand, (bool Success, string? Error)>
{
    public const string ReasonRequiredMessage = "سبب الإلغاء إلزامي.";

    /// <summary>
    /// PLAN-60 (§5): from Draft, PendingApproval or Approved only, with a reason; the transaction follows. From PendingApproval the
    /// engine's instance is withdrawn first: when it had ended, its outcome is applied and the request is judged again on the status that
    /// results. From Approved no saga exists, and a decision already issued stays linked, as history. After the execution: PLAN-63.
    /// </summary>
    public async Task<(bool Success, string? Error)> Handle(CancelSeparationCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return (false, ReasonRequiredMessage);
        var reason = request.Reason.Trim();

        var entity = await repository.GetSeparationRecordAsync(request.Id, cancellationToken);
        if (entity is null) return (false, null);
        if (SeparationWorkflowRules.RefusalFor(entity.Status, SeparationStatus.Cancelled) is { } refusal)
            return (false, refusal);

        if (entity.Status == SeparationStatus.PendingApproval)
        {
            var withdrawn = await approvals.WithdrawAsync(entity, request.ActorUserId, reason, cancellationToken);
            if (!withdrawn.Success && withdrawn.Error is not null)
                return (false, withdrawn.Error);

            // Withdrawn (back to Draft), or the instance had ended and its outcome was applied: judged again on what the record is now.
            await repository.ReloadAsync(entity, cancellationToken);
            if (SeparationWorkflowRules.RefusalFor(entity.Status, SeparationStatus.Cancelled) is { } after)
                return (false, after);
        }

        await SeparationStatusSync.TransitionAsync(repository, entity, SeparationStatus.Cancelled, request.ActorUserId, reason, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}
