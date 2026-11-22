using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Localization;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace Payroll.Application.Features.PayProfileChanges;

/// <summary>The approver's decision that applies a request at once (its effective date is today or earlier).</summary>
public sealed record PayProfileChangeApproval(Guid DecidedBy, string? DecidedByName, string? Note);

/// <summary>
/// PLAN-53 (§2, §3): the only place that creates a pay profile version for a change. It closes the previous version the day before the
/// new one starts, saves its history snapshot, creates the new version with every field of the previous one and the proposal, marks the
/// request applied, publishes <c>IPayProfileChangeApplied</c> through the outbox, and saves once: the profile, the request, the history,
/// the audit rows and the outbox rows commit together or not at all.
/// </summary>
public interface IPayProfileChangeApplier
{
    /// <summary>Whether the request can be applied now: an active profile it does not start before, the grade's bounds, and no run that
    /// settles its month for this employee. Reads only.</summary>
    Task<PayProfileChangeResult?> CheckAsync(PayProfileChangeRequest request, CancellationToken ct = default);

    /// <summary>
    /// Applies a request with one save: an approval that takes effect at once (<paramref name="approval"/> set: the decision is recorded and
    /// <c>IPayProfileChangeDecided</c> published in the same save), or a scheduled request that is due (<paramref name="approval"/> null).
    /// A refusal changes nothing in the database.
    /// </summary>
    Task<PayProfileChangeResult> ApplyAsync(PayProfileChangeRequest request, PayProfileChangeApproval? approval, CancellationToken ct = default);

    /// <summary>
    /// D-PAY-6: records an automatic change (a contract, a promotion, a transfer, a correction) in the same entity, applied at once: no
    /// decider, the original decision in <paramref name="sourceReference"/>. An open request of the same employee is superseded. A frozen
    /// month does not refuse it: a frozen run reads its own snapshot. The consumers move here in their own plans (PLAN-55, PAY-G).
    /// </summary>
    Task<PayProfileChangeResult> ApplySystemChangeAsync(
        Guid employeeId,
        PayProfileChangeKind kind,
        PayProfileChangeSource source,
        string? sourceReference,
        decimal? proposedBaseSalary,
        Guid? proposedPayGradeId,
        DateOnly effectiveFrom,
        CancellationToken ct = default);
}

public sealed class PayProfileChangeApplier(
    IPayrollRepository repository,
    IPayProfileChangeEventPublisher events,
    PayProfileChangeRules rules,
    IPayrollCalendar calendar) : IPayProfileChangeApplier
{
    public async Task<PayProfileChangeResult?> CheckAsync(PayProfileChangeRequest request, CancellationToken ct = default)
    {
        var profile = await repository.EmployeePayProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.EmployeeId == request.EmployeeId && p.IsActive, ct);
        return await CheckAsync(request, profile, ct);
    }

    private async Task<PayProfileChangeResult?> CheckAsync(PayProfileChangeRequest request, EmployeePayProfile? profile, CancellationToken ct)
    {
        if (profile is null)
            return Refuse(request, PayProfileChangeFailure.Invalid, PayProfileChangeMessages.NoActiveProfile);
        if (EmployeePayProfileVersioning.StartsBefore(request.EffectiveFrom, profile))
            return Refuse(request, PayProfileChangeFailure.Invalid, PayProfileChangeMessages.BeforeCurrentVersion);
        if (PayProfileChangeRequest.IsAutomatic(request.Source))
            return null;

        var bounds = await rules.CheckBoundsAsync(
            request.ProposedBaseSalary ?? profile.BaseSalary, request.ProposedPayGradeId ?? profile.PayGradeId, ct);
        if (bounds is not null)
            return Refuse(request, PayProfileChangeFailure.Invalid, bounds);

        var blocking = await rules.FindBlockingRunAsync(request.EmployeeId, request.EffectiveFrom, ct);
        return blocking is null ? null : Refuse(request, PayProfileChangeFailure.Conflict, blocking);
    }

    public async Task<PayProfileChangeResult> ApplyAsync(
        PayProfileChangeRequest request, PayProfileChangeApproval? approval, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var before = request.State;
        var expected = approval is null ? PayProfileChangeState.Scheduled : PayProfileChangeState.PendingApproval;
        if (before != expected)
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.Conflict,
                approval is null ? PayProfileChangeMessages.NotOpen : PayProfileChangeMessages.NotPending, request.Id, before);

        var profile = await repository.GetActiveProfileAsync(request.EmployeeId, ct);
        if (await CheckAsync(request, profile, ct) is { } refusal)
            return refusal;

        var now = calendar.UtcNow;
        if (approval is not null)
        {
            request.DecidedBy = approval.DecidedBy;
            request.DecidedByName = approval.DecidedByName;
            request.DecidedAt = now;
            request.DecisionNote = approval.Note;
        }

        var applied = await StageVersionAsync(request, profile!, now, ct);

        if (approval is not null)
        {
            await events.PublishDecidedAsync(new PayProfileChangeDecidedEvent(
                request.Id, request.EmployeeId, nameof(PayProfileChangeDecision.Approved), nameof(PayProfileChangeState.Applied),
                approval.DecidedBy, now), ct);
        }

        await events.PublishAppliedAsync(applied, ct);

        try
        {
            // The one save: the closed version, its history, the new version, the request, the audit rows and the outbox rows.
            await repository.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.Conflict, PayProfileChangeMessages.DecidedConcurrently, request.Id, before);
        }
        catch (DbUpdateException ex) when (ConcurrencyExtensions.IsUniqueViolation(ex))
        {
            // Another change of the same employee was applied between the read and the save (IX_EmployeePayProfiles_OneActive).
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.Conflict, PayProfileChangeMessages.DecidedConcurrently, request.Id, before);
        }

        return PayProfileChangeResult.Ok(request.Id, request.State);
    }

    public async Task<PayProfileChangeResult> ApplySystemChangeAsync(
        Guid employeeId,
        PayProfileChangeKind kind,
        PayProfileChangeSource source,
        string? sourceReference,
        decimal? proposedBaseSalary,
        Guid? proposedPayGradeId,
        DateOnly effectiveFrom,
        CancellationToken ct = default)
    {
        if (!PayProfileChangeRequest.IsAutomatic(source))
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.Invalid, PayProfileChangeMessages.AutomaticSourceOnly);
        if (sourceReference is { Length: > PayProfileChangeRequest.SourceReferenceMaxLength })
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.Invalid, PayProfileChangeMessages.SourceReferenceTooLong);

        // The kind says which parts of the proposal count; the other part is not applied.
        var salary = kind is PayProfileChangeKind.BaseSalary or PayProfileChangeKind.BaseSalaryAndGrade ? proposedBaseSalary : null;
        var grade = kind is PayProfileChangeKind.PayGrade or PayProfileChangeKind.BaseSalaryAndGrade ? proposedPayGradeId : null;
        if ((kind != PayProfileChangeKind.PayGrade && salary is null) || (kind != PayProfileChangeKind.BaseSalary && grade is null))
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.Invalid, PayProfileChangeMessages.NothingProposed);
        if (salary is <= 0)
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.Invalid, PayProfileChangeMessages.SalaryNotPositive);
        if (grade is Guid gradeId && !await repository.PayGrades.AsNoTracking().AnyAsync(g => g.Id == gradeId, ct))
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.Invalid, PayProfileChangeMessages.GradeNotFound);

        var profile = await repository.GetActiveProfileAsync(employeeId, ct);
        var now = calendar.UtcNow;
        var request = new PayProfileChangeRequest
        {
            EmployeeId = employeeId,
            OrgUnitId = profile?.OrgUnitId,
            ChangeKind = kind,
            Source = source,
            SourceReference = sourceReference,
            CurrentProfileId = profile?.Id,
            CurrentBaseSalary = profile?.BaseSalary,
            CurrentPayGradeId = profile?.PayGradeId,
            ProposedBaseSalary = salary,
            ProposedPayGradeId = grade,
            EffectiveFrom = effectiveFrom,
            Reason = $"تغيير آلي: {EnumDisplayExtensions.GetDisplayName(source)}",
            // Applied at once: never an open request, so the one-open index does not see it.
            State = PayProfileChangeState.Applied,
            RequestedBy = Guid.Empty,
            RequestedByName = PayProfileChangeRequest.SystemActorName,
            RequestedAt = now,
        };

        if (await CheckAsync(request, profile, ct) is { } refusal)
            return refusal with { RequestId = null, State = null };

        // An open request of the same employee was written against the version this change replaces: it is superseded.
        var open = await repository.PayProfileChangeRequests
            .Where(r => r.EmployeeId == employeeId
                        && (r.State == PayProfileChangeState.PendingApproval || r.State == PayProfileChangeState.Scheduled))
            .ToListAsync(ct);
        foreach (var superseded in open)
        {
            superseded.State = PayProfileChangeState.Superseded;
            superseded.SupersededByRequestId = request.Id;
        }

        await repository.AddAsync(request, ct);
        var applied = await StageVersionAsync(request, profile!, now, ct);
        await events.PublishAppliedAsync(applied, ct);

        try
        {
            await repository.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.Conflict, PayProfileChangeMessages.DecidedConcurrently);
        }
        catch (DbUpdateException ex) when (ConcurrencyExtensions.IsUniqueViolation(ex))
        {
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.Conflict, PayProfileChangeMessages.DecidedConcurrently);
        }

        return PayProfileChangeResult.Ok(request.Id, request.State);
    }

    /// <summary>
    /// The version change itself, in the unit of work and unsaved: close the previous version, snapshot it, add the next version with
    /// every field carried, mark the request applied. Returns the event of the application.
    /// </summary>
    private async Task<PayProfileChangeAppliedEvent> StageVersionAsync(
        PayProfileChangeRequest request, EmployeePayProfile previous, DateTime now, CancellationToken ct)
    {
        var previousSalary = previous.BaseSalary;
        EmployeePayProfileVersioning.Close(previous, request.EffectiveFrom);
        await repository.AddAsync(EmployeePayProfileRules.CreateHistorySnapshot(previous, request.Reason), ct);

        var next = EmployeePayProfileVersioning.NextVersion(
            previous, request.EffectiveFrom, request.ProposedBaseSalary, request.ProposedPayGradeId);
        await repository.AddAsync(next, ct);

        request.State = PayProfileChangeState.Applied;
        request.AppliedAt = now;
        request.AppliedProfileId = next.Id;
        request.LastApplyError = null;

        var gradeCode = next.PayGradeId is Guid gradeId
            ? await repository.PayGrades.AsNoTracking().Where(g => g.Id == gradeId).Select(g => g.GradeCode).FirstOrDefaultAsync(ct)
            : null;

        return new PayProfileChangeAppliedEvent(
            request.Id, request.EmployeeId, next.Id, previous.Id, request.EffectiveFrom, request.Source.ToString(), request.SourceReference,
            gradeCode, next.BaseSalary, previousSalary, now);
    }

    private static PayProfileChangeResult Refuse(PayProfileChangeRequest request, PayProfileChangeFailure failure, string error) =>
        PayProfileChangeResult.Fail(failure, error, request.Id, request.State);
}
