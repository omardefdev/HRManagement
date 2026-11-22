using HRManagement.ServiceDefaults;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.PayProfileChanges;
using Payroll.Application.Options;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Application.Features.ReferenceChanges;

/// <summary>
/// PLAN-53 (§4, D-53-4): whoever holds <c>Payroll.MasterData</c> requests a change of a salary scale or a grade (create, update,
/// delete), with a reason and a legal reference. Exactly one of <paramref name="Grade"/> and <paramref name="Scale"/> is given for a
/// creation or an update; neither for a deletion.
/// </summary>
public sealed record SubmitPayrollReferenceChangeCommand(
    PayrollReferenceEntityType EntityType,
    PayrollReferenceOperation Operation,
    Guid? EntityId,
    PayGradeChange? Grade,
    SalaryScaleChange? Scale,
    string? Reason,
    string? LegalReference,
    Guid ActorId,
    string? ActorName) : IRequest<PayrollReferenceChangeResult>;

/// <summary>PLAN-53: a payroll approver, who is not the requester, approves (the change is applied in the same save) or rejects.</summary>
public sealed record DecidePayrollReferenceChangeCommand(
    Guid RequestId, PayProfileChangeDecision Decision, string? Note, Guid ActorId, string? ActorName) : IRequest<PayrollReferenceChangeResult>;

/// <summary>PLAN-53: the requester cancels his own pending request, or an approver cancels any.</summary>
public sealed record CancelPayrollReferenceChangeCommand(
    Guid RequestId, string? Reason, Guid ActorId, string? ActorName, bool ActorCanApprove) : IRequest<PayrollReferenceChangeResult>;

/// <summary>
/// The rules and the application of a reference change, shared by the submission and the approval: the same validation runs at both,
/// and the approval applies the change in its own save. The only writer of salary scales and grades besides the seed.
/// </summary>
public sealed class PayrollReferenceChangeApplier(IPayrollRepository repository)
{
    /// <summary>The proposal against the rules and the data as they are now. Null when it may be applied.</summary>
    public async Task<(PayProfileChangeFailure Failure, string Error)?> ValidateAsync(
        PayrollReferenceEntityType type, PayrollReferenceOperation operation, Guid? entityId, PayGradeChange? grade, SalaryScaleChange? scale,
        CancellationToken ct)
    {
        if (operation != PayrollReferenceOperation.Create)
        {
            var exists = type == PayrollReferenceEntityType.PayGrade
                ? await repository.PayGrades.AsNoTracking().AnyAsync(g => g.Id == entityId, ct)
                : await repository.SalaryScales.AsNoTracking().AnyAsync(s => s.Id == entityId, ct);
            if (entityId is null || !exists)
                return (PayProfileChangeFailure.NotFound, PayrollReferenceChangeMessages.RecordNotFound);
        }

        if (operation == PayrollReferenceOperation.Delete)
            return type == PayrollReferenceEntityType.PayGrade
                ? await ValidateGradeDeleteAsync(entityId!.Value, ct)
                : await ValidateScaleDeleteAsync(entityId!.Value, ct);

        return type == PayrollReferenceEntityType.PayGrade
            ? await ValidateGradeAsync(operation == PayrollReferenceOperation.Create ? null : entityId, grade, ct)
            : await ValidateScaleAsync(operation == PayrollReferenceOperation.Create ? null : entityId, scale, ct);
    }

    private async Task<(PayProfileChangeFailure, string)?> ValidateGradeAsync(Guid? id, PayGradeChange? grade, CancellationToken ct)
    {
        if (grade is null)
            return (PayProfileChangeFailure.Invalid, PayrollReferenceChangeMessages.ProposalRequired);
        if (string.IsNullOrWhiteSpace(grade.GradeCode) || string.IsNullOrWhiteSpace(grade.GradeNameAr))
            return (PayProfileChangeFailure.Invalid, PayrollReferenceChangeMessages.GradeCodeRequired);
        if (grade.GradeCode.Length > 50 || grade.GradeNameAr.Length > 200)
            return (PayProfileChangeFailure.Invalid, PayrollReferenceChangeMessages.GradeTextTooLong);
        if (grade.BaseSalary < 0 || grade.MinSalary < 0 || grade.MaxSalary < 0)
            return (PayProfileChangeFailure.Invalid, PayrollReferenceChangeMessages.GradeAmountsNegative);
        if (grade.MinSalary > grade.BaseSalary || grade.BaseSalary > grade.MaxSalary)
            return (PayProfileChangeFailure.Invalid, PayrollReferenceChangeMessages.GradeBoundsOrder);
        if (!await repository.SalaryScales.AsNoTracking().AnyAsync(s => s.Id == grade.SalaryScaleId, ct))
            return (PayProfileChangeFailure.Invalid, PayrollReferenceChangeMessages.ScaleNotFound);
        if (await repository.PayGrades.AsNoTracking()
                .AnyAsync(g => g.SalaryScaleId == grade.SalaryScaleId && g.GradeCode == grade.GradeCode && g.Id != id, ct))
            return (PayProfileChangeFailure.Conflict, PayrollReferenceChangeMessages.GradeCodeTaken);
        return null;
    }

    private async Task<(PayProfileChangeFailure, string)?> ValidateScaleAsync(Guid? id, SalaryScaleChange? scale, CancellationToken ct)
    {
        if (scale is null)
            return (PayProfileChangeFailure.Invalid, PayrollReferenceChangeMessages.ProposalRequired);
        if (string.IsNullOrWhiteSpace(scale.Code) || string.IsNullOrWhiteSpace(scale.NameAr))
            return (PayProfileChangeFailure.Invalid, PayrollReferenceChangeMessages.ScaleCodeRequired);
        if (scale.Code.Length > 50 || scale.NameAr.Length > 200)
            return (PayProfileChangeFailure.Invalid, PayrollReferenceChangeMessages.ScaleTextTooLong);
        if (scale.EffectiveTo is DateOnly to && to < scale.EffectiveFrom)
            return (PayProfileChangeFailure.Invalid, PayrollReferenceChangeMessages.ScaleDatesOrder);
        if (await repository.SalaryScales.AsNoTracking().AnyAsync(s => s.Code == scale.Code && s.Id != id, ct))
            return (PayProfileChangeFailure.Conflict, PayrollReferenceChangeMessages.ScaleCodeTaken);
        return null;
    }

    /// <summary>A grade that any pay profile names (active or closed), or that an open salary change request names, is not deleted.</summary>
    private async Task<(PayProfileChangeFailure, string)?> ValidateGradeDeleteAsync(Guid id, CancellationToken ct)
    {
        var named = await repository.EmployeePayProfiles.AsNoTracking().AnyAsync(p => p.PayGradeId == id, ct)
                    || await repository.PayProfileChangeRequests.AsNoTracking().AnyAsync(
                        r => (r.State == PayProfileChangeState.PendingApproval || r.State == PayProfileChangeState.Scheduled)
                             && (r.ProposedPayGradeId == id || r.CurrentPayGradeId == id), ct);
        return named ? (PayProfileChangeFailure.Conflict, PayrollReferenceChangeMessages.GradeInUse) : null;
    }

    /// <summary>A scale that has grades is not deleted (its grades go first, each by its own request).</summary>
    private async Task<(PayProfileChangeFailure, string)?> ValidateScaleDeleteAsync(Guid id, CancellationToken ct) =>
        await repository.PayGrades.AsNoTracking().AnyAsync(g => g.SalaryScaleId == id, ct)
            ? (PayProfileChangeFailure.Conflict, PayrollReferenceChangeMessages.ScaleHasGrades)
            : null;

    /// <summary>The change itself, in the unit of work and unsaved: the caller's one save writes it with the request.</summary>
    public async Task ApplyAsync(PayrollReferenceChangeRequest request, DateTime now, CancellationToken ct)
    {
        if (request.EntityType == PayrollReferenceEntityType.PayGrade)
        {
            var proposal = PayrollReferenceChangeJson.Read<PayGradeChange>(request.ProposedJson);
            switch (request.Operation)
            {
                case PayrollReferenceOperation.Create:
                    var created = new PayGrade();
                    proposal!.ApplyTo(created);
                    await repository.AddAsync(created, ct);
                    request.EntityId = created.Id;
                    break;
                case PayrollReferenceOperation.Update:
                    proposal!.ApplyTo(await repository.PayGrades.FirstAsync(g => g.Id == request.EntityId, ct));
                    break;
                case PayrollReferenceOperation.Delete:
                    repository.Remove(await repository.PayGrades.FirstAsync(g => g.Id == request.EntityId, ct));
                    break;
            }
        }
        else
        {
            var proposal = PayrollReferenceChangeJson.Read<SalaryScaleChange>(request.ProposedJson);
            switch (request.Operation)
            {
                case PayrollReferenceOperation.Create:
                    var created = new SalaryScale();
                    proposal!.ApplyTo(created);
                    await repository.AddAsync(created, ct);
                    request.EntityId = created.Id;
                    break;
                case PayrollReferenceOperation.Update:
                    proposal!.ApplyTo(await repository.SalaryScales.FirstAsync(s => s.Id == request.EntityId, ct));
                    break;
                case PayrollReferenceOperation.Delete:
                    repository.Remove(await repository.SalaryScales.FirstAsync(s => s.Id == request.EntityId, ct));
                    break;
            }
        }

        request.State = PayrollReferenceChangeState.Applied;
        request.AppliedAt = now;
    }

    /// <summary>What a save that the database refused means: a code taken meanwhile, a record that came into use, a concurrent decision.</summary>
    public static string? RefusalOf(Exception exception, PayrollReferenceChangeRequest request)
    {
        if (exception is DbUpdateConcurrencyException)
            return PayrollReferenceChangeMessages.DecidedConcurrently;
        if (ConcurrencyExtensions.IsUniqueViolation(exception))
            return request.EntityType == PayrollReferenceEntityType.PayGrade
                ? PayrollReferenceChangeMessages.GradeCodeTaken
                : PayrollReferenceChangeMessages.ScaleCodeTaken;
        if (IsForeignKeyViolation(exception))
            return request.EntityType == PayrollReferenceEntityType.PayGrade
                ? PayrollReferenceChangeMessages.GradeInUse
                : PayrollReferenceChangeMessages.ScaleHasGrades;
        return null;
    }

    /// <summary>The Restrict relations of migration PAY053 answered (PostgreSQL 23503; SQLite's FOREIGN KEY constraint in tests).</summary>
    private static bool IsForeignKeyViolation(Exception exception)
    {
        for (Exception? ex = exception; ex is not null; ex = ex.InnerException)
        {
            var type = ex.GetType();
            if (string.Equals(type.Name, "PostgresException", StringComparison.Ordinal)
                && type.GetProperty("SqlState")?.GetValue(ex) as string == "23503")
                return true;
            if (string.Equals(type.Name, "SqliteException", StringComparison.Ordinal)
                && ex.Message.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

public sealed class SubmitPayrollReferenceChangeHandler(
    IPayrollRepository repository,
    PayrollReferenceChangeApplier applier,
    IPayrollCalendar calendar,
    IOptions<PayrollMasterDataOptions> options,
    IHostEnvironment? environment = null) : IRequestHandler<SubmitPayrollReferenceChangeCommand, PayrollReferenceChangeResult>
{
    public async Task<PayrollReferenceChangeResult> Handle(SubmitPayrollReferenceChangeCommand request, CancellationToken cancellationToken)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
            return Invalid(PayrollReferenceChangeMessages.ReasonRequired);
        if (reason.Length > PayProfileChangeRequest.ReasonMaxLength)
            return Invalid(PayrollReferenceChangeMessages.ReasonTooLong);
        var legalReference = request.LegalReference?.Trim();
        if (string.IsNullOrEmpty(legalReference))
            return Invalid(PayrollReferenceChangeMessages.LegalReferenceRequired);
        if (legalReference.Length > PayProfileChangeRequest.LegalReferenceMaxLength)
            return Invalid(PayrollReferenceChangeMessages.LegalReferenceTooLong);

        var grade = request.EntityType == PayrollReferenceEntityType.PayGrade ? Trimmed(request.Grade) : null;
        var scale = request.EntityType == PayrollReferenceEntityType.SalaryScale ? Trimmed(request.Scale) : null;
        if (await applier.ValidateAsync(request.EntityType, request.Operation, request.EntityId, grade, scale, cancellationToken) is { } broken)
            return PayrollReferenceChangeResult.Fail(broken.Failure, broken.Error);

        var entityId = request.Operation == PayrollReferenceOperation.Create ? null : request.EntityId;
        string? currentJson = null;
        string? label;
        if (request.EntityType == PayrollReferenceEntityType.PayGrade)
        {
            var current = entityId is null ? null : await repository.PayGrades.AsNoTracking().FirstAsync(g => g.Id == entityId, cancellationToken);
            if (current is not null)
                currentJson = PayrollReferenceChangeJson.Write(PayGradeChange.From(current));
            if (request.Operation == PayrollReferenceOperation.Update && PayGradeChange.From(current!) == grade)
                return Invalid(PayrollReferenceChangeMessages.NothingChanges);
            label = current is not null ? $"{current.GradeCode} — {current.GradeNameAr}" : $"{grade!.GradeCode} — {grade.GradeNameAr}";
        }
        else
        {
            var current = entityId is null ? null : await repository.SalaryScales.AsNoTracking().FirstAsync(s => s.Id == entityId, cancellationToken);
            if (current is not null)
                currentJson = PayrollReferenceChangeJson.Write(SalaryScaleChange.From(current));
            if (request.Operation == PayrollReferenceOperation.Update && SalaryScaleChange.From(current!) == scale)
                return Invalid(PayrollReferenceChangeMessages.NothingChanges);
            label = current is not null ? $"{current.Code} — {current.NameAr}" : $"{scale!.Code} — {scale.NameAr}";
        }

        if (entityId is not null && await repository.PayrollReferenceChangeRequests.AsNoTracking().AnyAsync(
                r => r.EntityType == request.EntityType && r.EntityId == entityId && r.State == PayrollReferenceChangeState.PendingApproval,
                cancellationToken))
            return PayrollReferenceChangeResult.Fail(PayProfileChangeFailure.Conflict, PayrollReferenceChangeMessages.OpenRequestExists);

        var now = calendar.UtcNow;
        var entity = new PayrollReferenceChangeRequest
        {
            EntityType = request.EntityType,
            EntityId = entityId,
            EntityLabel = label.Length > PayrollReferenceChangeRequest.LabelMaxLength ? label[..PayrollReferenceChangeRequest.LabelMaxLength] : label,
            Operation = request.Operation,
            ProposedJson = request.Operation == PayrollReferenceOperation.Delete
                ? null
                : grade is not null ? PayrollReferenceChangeJson.Write(grade) : PayrollReferenceChangeJson.Write(scale!),
            CurrentJson = currentJson,
            Reason = reason,
            LegalReference = legalReference,
            State = PayrollReferenceChangeState.PendingApproval,
            RequestedBy = request.ActorId,
            RequestedByName = request.ActorName,
            RequestedAt = now,
        };
        await repository.AddAsync(entity, cancellationToken);

        // Development only, and only when the setting says so: the request is applied at once, by the code the approval runs.
        if (!PayrollMasterDataApproval.IsRequired(options.Value, environment))
            await applier.ApplyAsync(entity, now, cancellationToken);

        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ConcurrencyExtensions.IsUniqueViolation(ex) || PayrollReferenceChangeApplier.RefusalOf(ex, entity) is not null)
        {
            // The one-open index decided a race between two requests for the same record; or, applied at once, the database refused it.
            var error = entity.State == PayrollReferenceChangeState.PendingApproval
                ? PayrollReferenceChangeMessages.OpenRequestExists
                : PayrollReferenceChangeApplier.RefusalOf(ex, entity) ?? PayrollReferenceChangeMessages.DecidedConcurrently;
            return PayrollReferenceChangeResult.Fail(PayProfileChangeFailure.Conflict, error);
        }

        return PayrollReferenceChangeResult.Ok(entity.Id, entity.State);
    }

    private static PayGradeChange? Trimmed(PayGradeChange? grade) =>
        grade is null ? null : grade with { GradeCode = grade.GradeCode?.Trim() ?? string.Empty, GradeNameAr = grade.GradeNameAr?.Trim() ?? string.Empty };

    private static SalaryScaleChange? Trimmed(SalaryScaleChange? scale) =>
        scale is null ? null : scale with { Code = scale.Code?.Trim() ?? string.Empty, NameAr = scale.NameAr?.Trim() ?? string.Empty };

    private static PayrollReferenceChangeResult Invalid(string error) => PayrollReferenceChangeResult.Fail(PayProfileChangeFailure.Invalid, error);
}

public sealed class DecidePayrollReferenceChangeHandler(
    IPayrollRepository repository, PayrollReferenceChangeApplier applier, IPayrollCalendar calendar)
    : IRequestHandler<DecidePayrollReferenceChangeCommand, PayrollReferenceChangeResult>
{
    public async Task<PayrollReferenceChangeResult> Handle(DecidePayrollReferenceChangeCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.PayrollReferenceChangeRequests.FirstOrDefaultAsync(r => r.Id == request.RequestId, cancellationToken);
        if (entity is null)
            return PayrollReferenceChangeResult.Fail(PayProfileChangeFailure.NotFound, PayrollReferenceChangeMessages.NotFound);

        var before = entity.State;
        PayrollReferenceChangeResult Refuse(PayProfileChangeFailure failure, string error) =>
            PayrollReferenceChangeResult.Fail(failure, error, entity.Id, before);

        // The approver is another person: the rule is on the user, so hr-admin, who holds both policies, cannot decide his own request.
        if (entity.RequestedBy == request.ActorId)
            return Refuse(PayProfileChangeFailure.SelfDecision, PayProfileChangeMessages.SelfDecision);
        if (before != PayrollReferenceChangeState.PendingApproval)
            return Refuse(PayProfileChangeFailure.Conflict, PayrollReferenceChangeMessages.NotPending);

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is { Length: > PayProfileChangeRequest.NoteMaxLength })
            return Refuse(PayProfileChangeFailure.Invalid, PayrollReferenceChangeMessages.NoteTooLong);

        var now = calendar.UtcNow;
        if (request.Decision == PayProfileChangeDecision.Rejected)
        {
            if (note is null)
                return Refuse(PayProfileChangeFailure.Invalid, PayrollReferenceChangeMessages.RejectNoteRequired);
            entity.State = PayrollReferenceChangeState.Rejected;
        }
        else
        {
            // The rules of the submission again, against the data as they are now: a code taken meanwhile, a grade that came into use.
            var broken = await applier.ValidateAsync(
                entity.EntityType, entity.Operation, entity.EntityId,
                entity.EntityType == PayrollReferenceEntityType.PayGrade ? PayrollReferenceChangeJson.Read<PayGradeChange>(entity.ProposedJson) : null,
                entity.EntityType == PayrollReferenceEntityType.SalaryScale ? PayrollReferenceChangeJson.Read<SalaryScaleChange>(entity.ProposedJson) : null,
                cancellationToken);
            if (broken is { } refusal)
                return Refuse(refusal.Failure == PayProfileChangeFailure.NotFound ? PayProfileChangeFailure.Conflict : refusal.Failure, refusal.Error);

            await applier.ApplyAsync(entity, now, cancellationToken);
        }

        entity.DecidedBy = request.ActorId;
        entity.DecidedByName = request.ActorName;
        entity.DecidedAt = now;
        entity.DecisionNote = note;

        try
        {
            // The one save: the decision and, for an approval, the change of the scale or the grade.
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (PayrollReferenceChangeApplier.RefusalOf(ex, entity) is { } error)
        {
            return Refuse(PayProfileChangeFailure.Conflict, error);
        }

        return PayrollReferenceChangeResult.Ok(entity.Id, entity.State);
    }
}

public sealed class CancelPayrollReferenceChangeHandler(IPayrollRepository repository, IPayrollCalendar calendar)
    : IRequestHandler<CancelPayrollReferenceChangeCommand, PayrollReferenceChangeResult>
{
    public async Task<PayrollReferenceChangeResult> Handle(CancelPayrollReferenceChangeCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.PayrollReferenceChangeRequests.FirstOrDefaultAsync(r => r.Id == request.RequestId, cancellationToken);
        if (entity is null)
            return PayrollReferenceChangeResult.Fail(PayProfileChangeFailure.NotFound, PayrollReferenceChangeMessages.NotFound);

        var before = entity.State;
        if (!request.ActorCanApprove && entity.RequestedBy != request.ActorId)
            return PayrollReferenceChangeResult.Fail(PayProfileChangeFailure.Forbidden, PayrollReferenceChangeMessages.CancelNotAllowed, entity.Id, before);
        if (!entity.IsOpen)
            return PayrollReferenceChangeResult.Fail(PayProfileChangeFailure.Conflict, PayrollReferenceChangeMessages.NotPending, entity.Id, before);

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
            return PayrollReferenceChangeResult.Fail(PayProfileChangeFailure.Invalid, PayrollReferenceChangeMessages.CancelReasonRequired, entity.Id, before);
        if (reason.Length > PayProfileChangeRequest.NoteMaxLength)
            return PayrollReferenceChangeResult.Fail(PayProfileChangeFailure.Invalid, PayrollReferenceChangeMessages.NoteTooLong, entity.Id, before);

        entity.State = PayrollReferenceChangeState.Cancelled;
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
            return PayrollReferenceChangeResult.Fail(PayProfileChangeFailure.Conflict, PayrollReferenceChangeMessages.DecidedConcurrently, entity.Id, before);
        }

        return PayrollReferenceChangeResult.Ok(entity.Id, entity.State);
    }
}
