using HRManagement.ServiceDefaults;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Application.Features.PayProfileChanges;

/// <summary>
/// PLAN-53 (§2): a payroll processor requests a change of an employee's base salary or grade. The form, the API and the PUT alias send
/// this one command. <paramref name="Scope"/> is the caller's branch scope: an employee outside it does not exist for him (404).
/// </summary>
public sealed record SubmitPayProfileChangeCommand(
    Guid EmployeeId,
    decimal? ProposedBaseSalary,
    Guid? ProposedPayGradeId,
    DateOnly EffectiveFrom,
    string? Reason,
    string? LegalReference,
    PayProfileChangeSource Source,
    string? SourceReference,
    Guid ActorId,
    string? ActorName,
    OrgAccessScope? Scope = null) : IRequest<PayProfileChangeResult>;

/// <summary>PLAN-53: a payroll approver, who is not the requester, approves or rejects a pending request.</summary>
public sealed record DecidePayProfileChangeCommand(
    Guid RequestId,
    PayProfileChangeDecision Decision,
    string? Note,
    Guid ActorId,
    string? ActorName,
    OrgAccessScope? Scope = null) : IRequest<PayProfileChangeResult>;

/// <summary>PLAN-53: the requester cancels his own open request, or an approver cancels any (<paramref name="ActorCanApprove"/>).</summary>
public sealed record CancelPayProfileChangeCommand(
    Guid RequestId,
    string? Reason,
    Guid ActorId,
    string? ActorName,
    bool ActorCanApprove,
    OrgAccessScope? Scope = null) : IRequest<PayProfileChangeResult>;

public sealed class SubmitPayProfileChangeHandler(
    IPayrollRepository repository,
    IPayProfileChangeEventPublisher events,
    PayProfileChangeRules rules,
    IPayrollCalendar calendar) : IRequestHandler<SubmitPayProfileChangeCommand, PayProfileChangeResult>
{
    public async Task<PayProfileChangeResult> Handle(SubmitPayProfileChangeCommand request, CancellationToken cancellationToken)
    {
        if (PayProfileChangeRequest.IsAutomatic(request.Source))
            return Invalid(PayProfileChangeMessages.ManualSourceOnly);

        var profile = await repository.EmployeePayProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.EmployeeId == request.EmployeeId && p.IsActive, cancellationToken);

        // The scope first: an employee outside the caller's branch, or with no profile to place him in it, does not exist for him.
        if (!PayProfileChangeScope.Sees(request.Scope, profile?.OrgUnitId))
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.NotFound, PayProfileChangeMessages.EmployeeNotFound);
        if (profile is null)
            return Invalid(PayProfileChangeMessages.NoActiveProfile);

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
            return Invalid(PayProfileChangeMessages.ReasonRequired);
        if (reason.Length > PayProfileChangeRequest.ReasonMaxLength)
            return Invalid(PayProfileChangeMessages.ReasonTooLong);

        var legalReference = string.IsNullOrWhiteSpace(request.LegalReference) ? null : request.LegalReference.Trim();
        if (legalReference is null && rules.Options.RequireLegalReference)
            return Invalid(PayProfileChangeMessages.LegalReferenceRequired);
        if (legalReference is { Length: > PayProfileChangeRequest.LegalReferenceMaxLength })
            return Invalid(PayProfileChangeMessages.LegalReferenceTooLong);

        var sourceReference = string.IsNullOrWhiteSpace(request.SourceReference) ? null : request.SourceReference.Trim();
        if (sourceReference is { Length: > PayProfileChangeRequest.SourceReferenceMaxLength })
            return Invalid(PayProfileChangeMessages.SourceReferenceTooLong);

        if (request.ProposedBaseSalary is <= 0)
            return Invalid(PayProfileChangeMessages.SalaryNotPositive);
        if (PayProfileChangeRules.KindOf(profile, request.ProposedBaseSalary, request.ProposedPayGradeId) is not { } kind)
            return Invalid(PayProfileChangeMessages.NothingProposed);

        // Only what differs from the current profile is proposed: the rest is carried by the applier from the version in force then.
        var proposedSalary = kind == PayProfileChangeKind.PayGrade ? null : request.ProposedBaseSalary;
        var proposedGrade = kind == PayProfileChangeKind.BaseSalary ? null : request.ProposedPayGradeId;

        if (rules.CheckRequestedDate(request.EffectiveFrom) is { } dateError)
            return Invalid(dateError);
        if (Payroll.Domain.Services.EmployeePayProfileVersioning.StartsBefore(request.EffectiveFrom, profile))
            return Invalid(PayProfileChangeMessages.BeforeCurrentVersion);

        if (proposedGrade is Guid gradeId
            && !await repository.PayGrades.AsNoTracking().AnyAsync(g => g.Id == gradeId, cancellationToken))
            return Invalid(PayProfileChangeMessages.GradeNotFound);
        if (await rules.CheckBoundsAsync(proposedSalary ?? profile.BaseSalary, proposedGrade ?? profile.PayGradeId, cancellationToken) is { } bounds)
            return Invalid(bounds);

        // D-53-3: one open request per employee. The filtered unique index decides a race; this read gives the usual answer.
        if (await repository.PayProfileChangeRequests.AsNoTracking().AnyAsync(
                r => r.EmployeeId == request.EmployeeId
                     && (r.State == PayProfileChangeState.PendingApproval || r.State == PayProfileChangeState.Scheduled),
                cancellationToken))
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.Conflict, PayProfileChangeMessages.OpenRequestExists);

        var now = calendar.UtcNow;
        var entity = new PayProfileChangeRequest
        {
            EmployeeId = request.EmployeeId,
            OrgUnitId = profile.OrgUnitId,
            ChangeKind = kind,
            Source = request.Source,
            SourceReference = sourceReference,
            CurrentProfileId = profile.Id,
            CurrentBaseSalary = profile.BaseSalary,
            CurrentPayGradeId = profile.PayGradeId,
            ProposedBaseSalary = proposedSalary,
            ProposedPayGradeId = proposedGrade,
            EffectiveFrom = request.EffectiveFrom,
            Reason = reason,
            LegalReference = legalReference,
            State = PayProfileChangeState.PendingApproval,
            RequestedBy = request.ActorId,
            RequestedByName = request.ActorName,
            RequestedAt = now,
        };

        await repository.AddAsync(entity, cancellationToken);
        await events.PublishSubmittedAsync(new PayProfileChangeSubmittedEvent(
            entity.Id, entity.EmployeeId, kind.ToString(), entity.Source.ToString(), entity.EffectiveFrom, entity.RequestedBy, now), cancellationToken);

        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ConcurrencyExtensions.IsUniqueViolation(ex))
        {
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.Conflict, PayProfileChangeMessages.OpenRequestExists);
        }

        return PayProfileChangeResult.Ok(entity.Id, entity.State);
    }

    private static PayProfileChangeResult Invalid(string error) => PayProfileChangeResult.Fail(PayProfileChangeFailure.Invalid, error);
}

public sealed class DecidePayProfileChangeHandler(
    IPayrollRepository repository,
    IPayProfileChangeEventPublisher events,
    IPayProfileChangeApplier applier,
    IPayrollCalendar calendar) : IRequestHandler<DecidePayProfileChangeCommand, PayProfileChangeResult>
{
    public async Task<PayProfileChangeResult> Handle(DecidePayProfileChangeCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.PayProfileChangeRequests.FirstOrDefaultAsync(r => r.Id == request.RequestId, cancellationToken);
        if (entity is null || !PayProfileChangeScope.Sees(request.Scope, entity.OrgUnitId))
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.NotFound, PayProfileChangeMessages.NotFound);

        var before = entity.State;
        PayProfileChangeResult Refuse(PayProfileChangeFailure failure, string error) =>
            PayProfileChangeResult.Fail(failure, error, entity.Id, before);

        // Separation of duties lives here, not in the policies: hr-admin holds both, and still cannot decide his own request.
        if (entity.RequestedBy == request.ActorId)
            return Refuse(PayProfileChangeFailure.SelfDecision, PayProfileChangeMessages.SelfDecision);
        if (before != PayProfileChangeState.PendingApproval)
            return Refuse(PayProfileChangeFailure.Conflict, PayProfileChangeMessages.NotPending);

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is { Length: > PayProfileChangeRequest.NoteMaxLength })
            return Refuse(PayProfileChangeFailure.Invalid, PayProfileChangeMessages.NoteTooLong);

        var now = calendar.UtcNow;
        if (request.Decision == PayProfileChangeDecision.Rejected)
        {
            if (note is null)
                return Refuse(PayProfileChangeFailure.Invalid, PayProfileChangeMessages.RejectNoteRequired);
            entity.State = PayProfileChangeState.Rejected;
        }
        else
        {
            // Approve. The rules of the request are checked again; a refusal leaves the request pending.
            if (await applier.CheckAsync(entity, cancellationToken) is { } refusal)
                return refusal;

            // Due already: the applier records the decision, creates the version and publishes both events in its one save.
            if (entity.EffectiveFrom <= calendar.Today)
                return await applier.ApplyAsync(entity, new PayProfileChangeApproval(request.ActorId, request.ActorName, note), cancellationToken);

            entity.State = PayProfileChangeState.Scheduled;
        }

        entity.DecidedBy = request.ActorId;
        entity.DecidedByName = request.ActorName;
        entity.DecidedAt = now;
        entity.DecisionNote = note;

        await events.PublishDecidedAsync(new PayProfileChangeDecidedEvent(
            entity.Id, entity.EmployeeId, request.Decision.ToString(), entity.State.ToString(), request.ActorId, now), cancellationToken);

        try
        {
            // The one save: the decision and its outbox row.
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another approver decided the same request first (the row version): this decision wrote nothing.
            return Refuse(PayProfileChangeFailure.Conflict, PayProfileChangeMessages.DecidedConcurrently);
        }

        return PayProfileChangeResult.Ok(entity.Id, entity.State);
    }
}

public sealed class CancelPayProfileChangeHandler(IPayrollRepository repository, IPayrollCalendar calendar)
    : IRequestHandler<CancelPayProfileChangeCommand, PayProfileChangeResult>
{
    public async Task<PayProfileChangeResult> Handle(CancelPayProfileChangeCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.PayProfileChangeRequests.FirstOrDefaultAsync(r => r.Id == request.RequestId, cancellationToken);
        if (entity is null || !PayProfileChangeScope.Sees(request.Scope, entity.OrgUnitId))
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.NotFound, PayProfileChangeMessages.NotFound);

        var before = entity.State;
        if (!request.ActorCanApprove && entity.RequestedBy != request.ActorId)
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.Forbidden, PayProfileChangeMessages.CancelNotAllowed, entity.Id, before);
        if (!entity.IsOpen)
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.Conflict, PayProfileChangeMessages.NotOpen, entity.Id, before);

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.Invalid, PayProfileChangeMessages.CancelReasonRequired, entity.Id, before);
        if (reason.Length > PayProfileChangeRequest.NoteMaxLength)
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.Invalid, PayProfileChangeMessages.NoteTooLong, entity.Id, before);

        entity.State = PayProfileChangeState.Cancelled;
        entity.CancelledBy = request.ActorId;
        entity.CancelledByName = request.ActorName;
        entity.CancelledAt = calendar.UtcNow;
        entity.CancelReason = reason;

        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.Conflict, PayProfileChangeMessages.DecidedConcurrently, entity.Id, before);
        }

        return PayProfileChangeResult.Ok(entity.Id, entity.State);
    }
}
