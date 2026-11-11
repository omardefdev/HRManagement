using HRManagement.ServiceDefaults.Messaging;
using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.HireCases;
using HR.Contracts.Application.Features.LeaveRequests;
using HR.Contracts.Application.Features.SeparationRecords;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Application.Services;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Entities;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Contracts.Application.Features.InboundEvents;

/// <remarks>
/// FUN-029: the members after <c>RecruitmentApplicationId</c> come from ICandidateAccepted v4 (null/false for v3).
/// </remarks>
public record ProcessCandidateAcceptedCommand(
    Guid EmployeeId,
    string EmploymentCategory,
    DateOnly ExpectedStartDate,
    string? FullName = null,
    Guid? RecruitmentApplicationId = null,
    Guid? RequisitionId = null,
    Guid? OfferLetterId = null,
    decimal? OfferedSalary = null,
    string? GradeCode = null,
    string? LegalRegimeId = null,
    string? EmploymentType = null,
    bool IsInternalMobility = false) : IRequest;
public record ProcessProbationEvaluationCompletedCommand(Guid EmployeeId, Guid ContractId, ProbationResult Result, Guid? EvaluatedBy, DateOnly EvaluatedDate, string? Notes) : IRequest;
public record ProcessLeaveRequestedCommand(Guid RequestId, Guid EmployeeId, LeaveType LeaveType, DateOnly StartDate, DateOnly EndDate, int DurationDays, string? Notes, Guid? OrgUnitId = null) : IRequest;
public record ProcessAdvanceRequestedCommand(Guid RequestId, Guid EmployeeId, decimal Amount, string? Reason, Guid? OrgUnitId = null) : IRequest;
public record ProcessJobChangeRequestedCommand(Guid RequestId, Guid EmployeeId, string CurrentTitle, string NewTitle, DateOnly EffectiveDate, string? Reason) : IRequest;
public record ProcessSeparationRequestCommand(Guid EmployeeId, SeparationType SeparationType, DateOnly EffectiveDate, string? Reason, string? Documents) : IRequest;
public record ProcessTransactionRegisteredInDiwanCommand(Guid TransactionId, string DiwanNumber) : IRequest;

public class ProcessCandidateAcceptedHandler(
    IContractsRepository repository,
    ICodeGenerationClient codeClient,
    ILogger<ProcessCandidateAcceptedHandler> logger,
    IOptions<HireContractOptions> contractOptions,
    IEventPublisher eventPublisher,
    IOptions<HireChecklistOptions> checklistOptions) : IRequestHandler<ProcessCandidateAcceptedCommand>
{
    // PLAN-40: the helpers save inside; from a consumer this joins the consumer outbox transaction.
    public Task Handle(ProcessCandidateAcceptedCommand request, CancellationToken cancellationToken) =>
        repository.ExecuteAtomicAsync(
            async token =>
            {
                await ProcessAsync(request, token);
                return true;
            },
            cancellationToken);

    private async Task ProcessAsync(ProcessCandidateAcceptedCommand request, CancellationToken cancellationToken)
    {
        // FUN-029 (E2): defence in depth — HR.Core publishes IInternalPlacementCompleted, not this, for mobility.
        if (request.IsInternalMobility)
        {
            logger.LogInformation(
                "CandidateAccepted for employee {EmployeeId} is internal mobility — no Appointment opened.",
                request.EmployeeId);
            return;
        }

        var employmentType = HireEmploymentTypes.Normalize(request.EmploymentType, request.EmploymentCategory);
        var hasAppointment = await repository.ServiceTransactions.AnyAsync(
            x => x.EmployeeId == request.EmployeeId
                 && x.TransactionType == ServiceTransactionType.Appointment
                 && x.State != ServiceTransactionState.Cancelled
                 && x.State != ServiceTransactionState.Archived,
            cancellationToken);

        ServiceTransaction transaction;
        if (hasAppointment)
        {
            transaction = await repository.ServiceTransactions
                .Where(x => x.EmployeeId == request.EmployeeId && x.TransactionType == ServiceTransactionType.Appointment)
                .OrderByDescending(x => x.CreatedAt)
                .FirstAsync(cancellationToken);
        }
        else
        {
            var terms = new ContractOfferTerms(
                request.GradeCode,
                request.OfferedSalary,
                request.LegalRegimeId,
                employmentType,
                request.OfferLetterId);
            var unresolved = false;
            if (!await repository.Contracts.AnyAsync(x => x.EmployeeId == request.EmployeeId, cancellationToken))
            {
                // FUN-029 (F2): contract type from the regime map; DEFAULT only when the fallback is enabled.
                var resolution = await new ContractTypeResolver(repository, contractOptions.Value)
                    .ResolveAsync(request.LegalRegimeId, employmentType, cancellationToken);
                if (resolution.ContractType is ContractType contractType)
                {
                    await repository.AddAsync(
                        PendingContractProvisioning.CreatePending(
                            request.EmployeeId,
                            contractType.Id,
                            request.ExpectedStartDate,
                            PendingContractTermsText,
                            terms),
                        cancellationToken);
                }
                else
                {
                    unresolved = true;
                }
            }

            transaction = new ServiceTransaction
            {
                EmployeeId = request.EmployeeId,
                TransactionType = ServiceTransactionType.Appointment,
                // Notes stay human-readable; lineage is read from the columns (FUN-029).
                Notes = BuildHireNotes(request.FullName, request.EmploymentCategory, request.RecruitmentApplicationId)
            };
            ApplyLineage(transaction, request);
            if (unresolved)
                transaction.PendingContractTerms = JsonSerializer.Serialize(terms);
            await ServiceTransactionReferenceSupport.ApplyReferenceAsync(transaction, codeClient, cancellationToken);
            transaction.MoveTo(ServiceTransactionState.Submitted, byUserId: null, "Inbound employment event");
            transaction.MoveTo(ServiceTransactionState.UnderReview, byUserId: null, null);
            transaction.MoveTo(ServiceTransactionState.InProcess, byUserId: null, "بدء معالجة التعيين");
            await repository.AddAsync(transaction, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);

            await HireCaseSupport.SeedChecklistAsync(repository, transaction.Id, cancellationToken, checklistOptions.Value);
            await HireCaseSupport.MarkCandidateReceivedAsync(repository, transaction.Id, cancellationToken);
            await HireCaseSupport.MarkPersonalFileOpenedAsync(repository, request.EmployeeId, cancellationToken);
            if (unresolved)
                await FlagUnresolvedContractTypeAsync(transaction, request.LegalRegimeId, employmentType, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.FullName) || request.RecruitmentApplicationId.HasValue)
            transaction.Notes = BuildHireNotes(request.FullName, request.EmploymentCategory, request.RecruitmentApplicationId);
        ApplyLineage(transaction, request);

        await HireCaseSupport.SeedChecklistAsync(repository, transaction.Id, cancellationToken, checklistOptions.Value);
        await HireCaseSupport.MarkCandidateReceivedAsync(repository, transaction.Id, cancellationToken);
        await HireCaseSupport.MarkPersonalFileOpenedAsync(repository, request.EmployeeId, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }

    public const string PendingContractTermsText = "تم الإنشاء تلقائياً من حدث التوظيف";

    /// <summary>
    /// FUN-029 (F3): no silent fallback — the Appointment and its checklist exist, there is no contract, the
    /// ContractClassified item says why, and an intervention is opened for the hire case.
    /// </summary>
    [StagesOutboxMessages]
    private async Task FlagUnresolvedContractTypeAsync(
        ServiceTransaction transaction,
        string? legalRegimeId,
        string? employmentType,
        CancellationToken cancellationToken)
    {
        var reason = HireContractMessages.UnresolvedContractType(legalRegimeId, employmentType);
        var item = await repository.HireChecklists.FirstOrDefaultAsync(
            c => c.ServiceTransactionId == transaction.Id && c.Step == HireStep.ContractClassified,
            cancellationToken);
        if (item is not null)
            item.Notes = reason;

        await eventPublisher.PublishProcessInterventionRequestedAsync(
            SagaTypeNames.Hire,
            transaction.Id,
            reason,
            "HR.Contracts",
            transaction.TenantId == Guid.Empty ? null : transaction.TenantId,
            cancellationToken);
        HireContractMetrics.ContractTypeUnresolved.Add(1);
        logger.LogWarning(
            "Hire {TransactionId}: no contract type for regime {LegalRegimeId} / {EmploymentType}; intervention requested.",
            transaction.Id, legalRegimeId, employmentType);
    }

    /// <summary>FUN-029: lineage columns; a redelivery fills what is missing and never clears a value.</summary>
    private static void ApplyLineage(ServiceTransaction transaction, ProcessCandidateAcceptedCommand request)
    {
        transaction.RecruitmentApplicationId ??= request.RecruitmentApplicationId;
        transaction.RequisitionId ??= request.RequisitionId;
        transaction.OfferLetterId ??= request.OfferLetterId;
        transaction.ExpectedStartDate ??= request.ExpectedStartDate;
    }

    private const string RecruitmentApplicationNotePrefix = "recruitmentApplicationId:";

    private static string BuildHireNotes(string? fullName, string employmentCategory, Guid? recruitmentApplicationId)
    {
        var baseNote = string.IsNullOrWhiteSpace(fullName)
            ? $"مصدر توظيف: {employmentCategory}"
            : $"{fullName.Trim()} — مصدر توظيف: {employmentCategory}";

        return recruitmentApplicationId is Guid appId
            ? $"{baseNote} | {RecruitmentApplicationNotePrefix}{appId:D}"
            : baseNote;
    }

    /// <summary>
    /// Legacy notes parser. FUN-029: reads use <see cref="ServiceTransaction.RecruitmentApplicationId"/>; this stays only
    /// to document the format the FUN029_ContractOfferTerms backfill matches (and for its test).
    /// </summary>
    internal static Guid? ParseRecruitmentApplicationId(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return null;

        const string marker = RecruitmentApplicationNotePrefix;
        var index = notes.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return null;

        var value = notes[(index + marker.Length)..].Split('|', ' ', ';')[0].Trim();
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }
}

public class ProcessProbationEvaluationCompletedHandler(
    IContractsRepository repository,
    ICodeGenerationClient codeClient) : IRequestHandler<ProcessProbationEvaluationCompletedCommand>
{
    public async Task Handle(ProcessProbationEvaluationCompletedCommand request, CancellationToken cancellationToken)
    {
        await repository.AddAsync(new ProbationRecord
        {
            ContractId = request.ContractId,
            EmployeeId = request.EmployeeId,
            StartDate = request.EvaluatedDate.AddMonths(-3),
            EndDate = request.EvaluatedDate,
            Result = request.Result,
            DecisionBy = request.EvaluatedBy,
            DecisionDate = request.EvaluatedDate,
            Notes = request.Notes
        }, cancellationToken);

        var transaction = new ServiceTransaction
        {
            EmployeeId = request.EmployeeId,
            TransactionType = ServiceTransactionType.Confirmation,
            Notes = "Inbound probation evaluation"
        };
        await ServiceTransactionReferenceSupport.ApplyReferenceAsync(transaction, codeClient, cancellationToken);
        transaction.MoveTo(ServiceTransactionState.Submitted, null, null);
        transaction.MoveTo(ServiceTransactionState.UnderReview, null, null);
        transaction.MoveTo(ServiceTransactionState.InProcess, null, null);
        transaction.MoveTo(ServiceTransactionState.PendingApproval, null, null);
        await repository.AddAsync(transaction, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// A leave request that arrives from the self-service portal (ILeaveRequested).
/// <para>
/// PLAN-49 (G-POL-04, D-POL-14): it passes the local validation and the policy engine exactly as a request HR records does
/// (action Create); it used to skip both. The portal sends calendar days, so the working days are computed here, as on the HR
/// page, and that is the number stored and sent to the engine: the two channels state the same fact.
/// </para>
/// <para>
/// A refusal never drops the request and never fails the message (a consumer that throws is retried and then parked, and the
/// employee would see nothing): the request is created Rejected, with the reason at the head of its notes and its service
/// transaction cancelled, as a rejection by HR leaves it. The local validation refuses in every mode; the policy refuses only in
/// Enforce (in Shadow its decision is recorded by HR.Core and the request is created as before).
/// </para>
/// <para>
/// An outage is not a refusal. When, in Enforce, the engine cannot be reached, the client answers "not allowed, degraded": the
/// request is not written off as Rejected for good on that; the handler throws, the bus retries the message and parks it if the
/// outage lasts, and a replay decides it when the engine is back. (In Shadow an outage never refuses.)
/// </para>
/// </summary>
public class ProcessLeaveRequestedHandler(
    IContractsRepository repository,
    ICodeGenerationClient codeClient,
    IPolicyEnforceClient policyClient) : IRequestHandler<ProcessLeaveRequestedCommand>
{
    /// <summary>The head of the notes of a request created Rejected; the reason follows it.</summary>
    public const string RefusedNotesPrefix = "مرفوض آلياً: ";

    public const string RefusedTransactionReason = "رفض طلب إجازة وارد من الخدمة الذاتية";

    public async Task Handle(ProcessLeaveRequestedCommand request, CancellationToken cancellationToken)
    {
        // A redelivered event (PLAN-40's receipt comes first; this is the last line): no second request, and no second question
        // to the engine, so no second ledger row.
        if (await repository.LeaveRequests.AnyAsync(x => x.Id == request.RequestId, cancellationToken))
        {
            return;
        }

        // The reference number first: it is a call to HR.Core that can fail, and a message retried after the engine was asked
        // would ask it again (a second ledger row for one request). Only the saves below come after the question. The price,
        // in Enforce only: each attempt that ends in an engine outage has spent a number, and the bus makes 1 + RetryCount
        // attempts of a delivery (the numbers are not gapless, here as in the other inbound handlers). In Shadow an unanswered
        // question never stops the request, so no number is lost. Before Enforce: a stable request key on the number call.
        var transaction = new ServiceTransaction
        {
            EmployeeId = request.EmployeeId,
            TransactionType = ServiceTransactionType.LeaveAdvance,
            Notes = "Inbound ESS leave request"
        };
        await ServiceTransactionReferenceSupport.ApplyReferenceAsync(transaction, codeClient, cancellationToken);

        // Working days, whatever number the event carries (the portal counts calendar days).
        var durationDays = LeaveAndAdvanceRules.ComputeLeaveDurationDays(request.StartDate, request.EndDate);
        var entity = new LeaveRequest
        {
            Id = request.RequestId,
            EmployeeId = request.EmployeeId,
            OrgUnitId = request.OrgUnitId,
            LeaveType = request.LeaveType,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            DurationDays = durationDays,
            Notes = request.Notes,
            Status = ApprovalStatus.Pending
        };

        var peerLeaves = await repository.LeaveRequests.AsNoTracking()
            .Where(x => x.EmployeeId == request.EmployeeId)
            .ToListAsync(cancellationToken);
        var refusal = LeaveAndAdvanceRules.ValidateLeaveRequest(
            request.EmployeeId, request.StartDate, request.EndDate, durationDays, peerLeaves, null);
        if (refusal is null)
        {
            // The tenant is the entity's: empty until the save stamps it, so the engine takes the tenant of the call, which
            // the consumer filter set from the message (TenantIdFlow) and the service client forwards.
            var policyResult = await policyClient.EnforceAsync(
                PolicyDomainFactsBuilders.ForLeaveRequest(
                    entity.Id, entity.TenantId, durationDays, request.LeaveType.ToString(), LeavePolicyActions.Create),
                cancellationToken);
            // An outage (Enforce, fail closed) is not the policy's refusal: nothing is written, and the bus retries (an
            // exception its retry policy does not ignore), then parks the message in _error for a replay.
            if (!policyResult.Allowed && policyResult.Degraded)
                throw new PolicyEngineUnavailableException(HRManagement.SharedKernel.Policy.PolicyDomains.ContractsLeave, LeavePolicyActions.Create);
            if (!policyResult.Allowed)
                refusal = policyResult.Decision.DenyReason ?? LeavePolicyActions.DeniedMessage;
        }

        if (refusal is not null)
        {
            entity.Status = ApprovalStatus.Rejected;
            entity.Notes = string.IsNullOrWhiteSpace(request.Notes)
                ? RefusedNotesPrefix + refusal
                : RefusedNotesPrefix + refusal + "\n" + request.Notes.Trim();
            // No approver decided: the transaction is closed the way RejectLeaveRequestHandler closes one that never reached
            // an approval step.
            ServiceTransactionWorkflowHelper.AdvanceToCancelled(transaction, null, RefusedTransactionReason);
        }

        await repository.AddAsync(entity, cancellationToken);
        await repository.AddAsync(transaction, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        entity.ServiceTransactionId = transaction.Id;
        await repository.SaveChangesAsync(cancellationToken);
    }
}

public class ProcessAdvanceRequestedHandler(
    IContractsRepository repository,
    ICodeGenerationClient codeClient) : IRequestHandler<ProcessAdvanceRequestedCommand>
{
    public async Task Handle(ProcessAdvanceRequestedCommand request, CancellationToken cancellationToken)
    {
        if (await repository.AdvanceRequests.AnyAsync(x => x.Id == request.RequestId, cancellationToken))
        {
            return;
        }

        var transaction = new ServiceTransaction
        {
            EmployeeId = request.EmployeeId,
            TransactionType = ServiceTransactionType.LeaveAdvance,
            Notes = "Inbound ESS advance request"
        };
        await ServiceTransactionReferenceSupport.ApplyReferenceAsync(transaction, codeClient, cancellationToken);

        var entity = new AdvanceRequest
        {
            Id = request.RequestId,
            EmployeeId = request.EmployeeId,
            OrgUnitId = request.OrgUnitId,
            Amount = request.Amount,
            Reason = request.Reason,
            Status = ApprovalStatus.Pending
        };

        await repository.AddAsync(entity, cancellationToken);
        await repository.AddAsync(transaction, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        entity.ServiceTransactionId = transaction.Id;
        await repository.SaveChangesAsync(cancellationToken);
    }
}

public class ProcessJobChangeRequestedHandler(
    IContractsRepository repository,
    ICodeGenerationClient codeClient) : IRequestHandler<ProcessJobChangeRequestedCommand>
{
    public async Task Handle(ProcessJobChangeRequestedCommand request, CancellationToken cancellationToken)
    {
        if (await repository.AdministrativeDecisions.AnyAsync(x => x.Id == request.RequestId, cancellationToken))
            return;

        var transaction = new ServiceTransaction
        {
            EmployeeId = request.EmployeeId,
            TransactionType = ServiceTransactionType.JobUpdate,
            Notes = "Inbound ESS job change request"
        };
        await ServiceTransactionReferenceSupport.ApplyReferenceAsync(transaction, codeClient, cancellationToken);

        await repository.AddAsync(new AdministrativeDecision
        {
            Id = request.RequestId,
            RelatedEmployeeId = request.EmployeeId,
            Subject = $"تغيير وظيفي: {request.CurrentTitle} -> {request.NewTitle}",
            DecisionNumber = $"AUTO-{DateTime.UtcNow:yyyyMMddHHmmss}",
            IssuedDate = request.EffectiveDate,
            IssuedBy = ServerAssignedIdentity.InboundSystemActor
        }, cancellationToken);
        await repository.AddAsync(transaction, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }
}

public class ProcessSeparationRequestHandler(
    IContractsRepository repository,
    ICodeGenerationClient codeClient,
    IEmployeeLifecycleGate lifecycleGate) : IRequestHandler<ProcessSeparationRequestCommand>
{
    public async Task Handle(ProcessSeparationRequestCommand request, CancellationToken cancellationToken)
    {
        var existing = await repository.SeparationRecords
            .AnyAsync(r => r.EmployeeId == request.EmployeeId
                && r.EffectiveDate == request.EffectiveDate
                && r.SeparationType == request.SeparationType
                && !r.SeparationEventPublished, cancellationToken);
        if (existing)
            return;

        // PLAN-42 (G-SEP-12): no record under a legal hold, or when the hold cannot be checked. The message fails and stays in the
        // broker's retry and error path, so it is neither lost nor acted on.
        var (allowed, holdError) = await lifecycleGate.AssertCanStartSeparationAsync(request.EmployeeId, cancellationToken);
        if (!allowed)
            throw new InvalidOperationException(holdError);

        var entity = new SeparationRecord
        {
            EmployeeId = request.EmployeeId,
            SeparationType = request.SeparationType,
            EffectiveDate = request.EffectiveDate,
            Reason = request.Reason,
            Documents = request.Documents,
            SeparationEventPublished = false
        };
        await repository.AddAsync(entity, cancellationToken);
        var transaction = new ServiceTransaction
        {
            EmployeeId = request.EmployeeId,
            TransactionType = ServiceTransactionType.Separation,
            Notes = "Inbound separation request"
        };
        await ServiceTransactionReferenceSupport.ApplyReferenceAsync(transaction, codeClient, cancellationToken);
        await repository.AddAsync(transaction, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        await SeparationRecordSupport.SeedChecklistAsync(repository, entity.Id, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }
}

public class ProcessTransactionRegisteredInDiwanHandler(IContractsRepository repository) : IRequestHandler<ProcessTransactionRegisteredInDiwanCommand>
{
    public async Task Handle(ProcessTransactionRegisteredInDiwanCommand request, CancellationToken cancellationToken)
    {
        var transaction = await repository.GetServiceTransactionAsync(request.TransactionId, cancellationToken);
        if (transaction is null)
        {
            return;
        }

        var diwanNote = $"Diwan: {request.DiwanNumber.Trim()}";
        transaction.Notes = string.IsNullOrWhiteSpace(transaction.Notes)
            ? diwanNote
            : $"{transaction.Notes} | {diwanNote}";
        if (transaction.State == ServiceTransactionState.Completed)
        {
            transaction.MoveTo(ServiceTransactionState.Archived, null, "Registered in Diwan");
        }
        await repository.SaveChangesAsync(cancellationToken);
    }
}
