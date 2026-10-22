using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.HireCases;
using HR.Contracts.Application.Features.SeparationRecords;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Application.Services;
using HRManagement.ServiceDefaults.CodeGeneration;
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
    IEventPublisher eventPublisher) : IRequestHandler<ProcessCandidateAcceptedCommand>
{
    public async Task Handle(ProcessCandidateAcceptedCommand request, CancellationToken cancellationToken)
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

            await HireCaseSupport.SeedChecklistAsync(repository, transaction.Id, cancellationToken);
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

        await HireCaseSupport.SeedChecklistAsync(repository, transaction.Id, cancellationToken);
        await HireCaseSupport.MarkCandidateReceivedAsync(repository, transaction.Id, cancellationToken);
        await HireCaseSupport.MarkPersonalFileOpenedAsync(repository, request.EmployeeId, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }

    public const string PendingContractTermsText = "تم الإنشاء تلقائياً من حدث التوظيف";

    /// <summary>
    /// FUN-029 (F3): no silent fallback — the Appointment and its checklist exist, there is no contract, the
    /// ContractClassified item says why, and an intervention is opened for the hire case.
    /// </summary>
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

public class ProcessLeaveRequestedHandler(
    IContractsRepository repository,
    ICodeGenerationClient codeClient) : IRequestHandler<ProcessLeaveRequestedCommand>
{
    public async Task Handle(ProcessLeaveRequestedCommand request, CancellationToken cancellationToken)
    {
        if (await repository.LeaveRequests.AnyAsync(x => x.Id == request.RequestId, cancellationToken))
        {
            return;
        }

        var transaction = new ServiceTransaction
        {
            EmployeeId = request.EmployeeId,
            TransactionType = ServiceTransactionType.LeaveAdvance,
            Notes = "Inbound ESS leave request"
        };
        await ServiceTransactionReferenceSupport.ApplyReferenceAsync(transaction, codeClient, cancellationToken);

        var entity = new LeaveRequest
        {
            Id = request.RequestId,
            EmployeeId = request.EmployeeId,
            OrgUnitId = request.OrgUnitId,
            LeaveType = request.LeaveType,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            DurationDays = request.DurationDays,
            Notes = request.Notes,
            Status = ApprovalStatus.Pending
        };

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
    ICodeGenerationClient codeClient) : IRequestHandler<ProcessSeparationRequestCommand>
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
