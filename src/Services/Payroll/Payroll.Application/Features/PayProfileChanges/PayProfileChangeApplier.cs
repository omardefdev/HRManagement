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
/// PLAN-55 (§3): what the activation of a contract needs from the applier, and no other source gets. A first activation finds no
/// profile in force (only the inactive placeholder <c>IEmployeeCreated</c> wrote, or none), so the version starts from the employee's
/// latest profile, or is the first one. PAY-029 stays as it is: with no salary known the activation proposes nothing and still makes
/// its (unpriced) version, which <see cref="FirstVersionIsActive"/> says is active or not.
/// </summary>
/// <param name="FirstVersionIsActive">Whether the version is active when no profile was in force (PAY-029: false for an unpriced
/// profile under <c>Payroll:RequireBaseSalaryOnActivation</c>). Over a profile in force the new version is always active.</param>
/// <param name="HistoryReason">The reason on the history snapshot of the version it closes.</param>
public sealed record PayProfileContractActivation(bool FirstVersionIsActive, string HistoryReason);

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

    /// <summary>
    /// PLAN-55 (§3): the same, for the activation of a contract (<paramref name="activation"/> set; the source must be
    /// <c>ContractActivated</c>). It differs from every other automatic change in three ways, each PAY-029's or the first activation's:
    /// no profile in force is needed (the version starts from the employee's latest profile, whatever the date of a placeholder, or is
    /// the first); the proposal may be empty (no salary known: an unpriced version); and whatever the caller staged in the unit of work
    /// before the call (its adjustment, its review hold) is written by the same save as the version, the request row and the event.
    /// A refusal saves nothing: the caller clears the unit of work.
    /// </summary>
    Task<PayProfileChangeResult> ApplySystemChangeAsync(
        Guid employeeId,
        PayProfileChangeKind kind,
        PayProfileChangeSource source,
        string? sourceReference,
        decimal? proposedBaseSalary,
        Guid? proposedPayGradeId,
        DateOnly effectiveFrom,
        PayProfileContractActivation? activation,
        CancellationToken ct = default);
}

public sealed class PayProfileChangeApplier(
    IPayrollRepository repository,
    IPayProfileChangeEventPublisher events,
    PayProfileChangeRules rules,
    IPayrollCalendar calendar) : IPayProfileChangeApplier
{
    /// <summary>
    /// An applier for a handler built by hand, outside the host (a unit test that makes <c>HandleContractActivatedHandler</c> from a
    /// repository alone): the default rules, the system calendar, and no bus, so <b>no event is staged</b>. The host never uses it: its
    /// handlers are given the registered applier, whose events go through the outbox.
    /// </summary>
    public static PayProfileChangeApplier Detached(IPayrollRepository repository)
    {
        var calendar = new PayrollCalendar(TimeProvider.System);
        var rules = new PayProfileChangeRules(
            repository, calendar, Microsoft.Extensions.Options.Options.Create(new Options.PayProfileChangeOptions()));
        return new PayProfileChangeApplier(repository, NoPayProfileChangeEvents.Instance, rules, calendar);
    }

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
        // A person's request is applied only over the profile it was written against (a consumer may have changed it since).
        if (!PayProfileChangeRules.IsWrittenAgainst(request, profile))
            return Refuse(request, PayProfileChangeFailure.Conflict, PayProfileChangeMessages.ProfileChangedSinceRequest);

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

        var applied = await StageVersionAsync(request, [profile!], null, now, ct);

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

        var warning = await rules.HasCalculatedUnfrozenRegularRunAsync(request.EffectiveFrom, ct)
            ? PayProfileChangeMessages.CalculatedRunNeedsRecalculation
            : null;
        return PayProfileChangeResult.Ok(request.Id, request.State) with { Warning = warning };
    }

    public Task<PayProfileChangeResult> ApplySystemChangeAsync(
        Guid employeeId,
        PayProfileChangeKind kind,
        PayProfileChangeSource source,
        string? sourceReference,
        decimal? proposedBaseSalary,
        Guid? proposedPayGradeId,
        DateOnly effectiveFrom,
        CancellationToken ct = default) =>
        ApplySystemChangeAsync(employeeId, kind, source, sourceReference, proposedBaseSalary, proposedPayGradeId, effectiveFrom, null, ct);

    public async Task<PayProfileChangeResult> ApplySystemChangeAsync(
        Guid employeeId,
        PayProfileChangeKind kind,
        PayProfileChangeSource source,
        string? sourceReference,
        decimal? proposedBaseSalary,
        Guid? proposedPayGradeId,
        DateOnly effectiveFrom,
        PayProfileContractActivation? activation,
        CancellationToken ct = default)
    {
        if (!PayProfileChangeRequest.IsAutomatic(source))
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.Invalid, PayProfileChangeMessages.AutomaticSourceOnly);
        if (activation is not null && source != PayProfileChangeSource.ContractActivated)
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.Invalid, PayProfileChangeMessages.ActivationTermsForContractOnly);
        if (sourceReference is { Length: > PayProfileChangeRequest.SourceReferenceMaxLength })
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.Invalid, PayProfileChangeMessages.SourceReferenceTooLong);

        // The kind says which parts of the proposal count; the other part is not applied.
        var salary = kind is PayProfileChangeKind.BaseSalary or PayProfileChangeKind.BaseSalaryAndGrade ? proposedBaseSalary : null;
        var grade = kind is PayProfileChangeKind.PayGrade or PayProfileChangeKind.BaseSalaryAndGrade ? proposedPayGradeId : null;
        // A contract's activation may propose nothing: no salary is known, and PAY-029 still makes the (unpriced) version.
        if (activation is null
            && ((kind != PayProfileChangeKind.PayGrade && salary is null) || (kind != PayProfileChangeKind.BaseSalary && grade is null)))
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.Invalid, PayProfileChangeMessages.NothingProposed);
        if (salary is <= 0)
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.Invalid, PayProfileChangeMessages.SalaryNotPositive);
        if (grade is Guid gradeId && !await repository.PayGrades.AsNoTracking().AnyAsync(g => g.Id == gradeId, ct))
            return PayProfileChangeResult.Fail(PayProfileChangeFailure.Invalid, PayProfileChangeMessages.GradeNotFound);

        // What the version starts from: the profile in force; for a contract's activation, every profile of the employee (the latest
        // one carries the unit and the regime when none is in force).
        List<EmployeePayProfile> profiles;
        if (activation is null)
        {
            var inForce = await repository.GetActiveProfileAsync(employeeId, ct);
            profiles = inForce is null ? [] : [inForce];
        }
        else
        {
            profiles = await repository.EmployeePayProfiles.Where(p => p.EmployeeId == employeeId).ToListAsync(ct);
        }

        var profile = profiles.FirstOrDefault(p => p.IsActive);
        var startsFrom = EmployeePayProfileVersioning.PreviousOf(profiles);
        var now = calendar.UtcNow;
        var request = new PayProfileChangeRequest
        {
            EmployeeId = employeeId,
            // The unit the new version will carry: the key of the branch scope.
            OrgUnitId = startsFrom?.OrgUnitId,
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

        if (activation is null)
        {
            if (await CheckAsync(request, profile, ct) is { } refusal)
                return refusal with { RequestId = null, State = null };
        }
        else if (startsFrom is not null && EmployeePayProfileVersioning.StartsInside(effectiveFrom, startsFrom))
        {
            // The one rule left for an activation: its version takes no day that belongs to another (a placeholder holds none).
            return PayProfileChangeResult.Fail(
                PayProfileChangeFailure.Invalid,
                startsFrom.IsActive ? PayProfileChangeMessages.BeforeCurrentVersion : PayProfileChangeMessages.InsidePreviousVersion);
        }

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
        var applied = await StageVersionAsync(request, profiles, activation, now, ct);
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
    /// The version change itself, in the unit of work and unsaved: <see cref="EmployeePayProfileVersioning.CreateNextVersion"/> closes
    /// the previous version and makes the next one with every field carried; this adds the next version and the history snapshot, and
    /// marks the request applied. Returns the event of the application.
    /// </summary>
    private async Task<PayProfileChangeAppliedEvent> StageVersionAsync(
        PayProfileChangeRequest request,
        IReadOnlyCollection<EmployeePayProfile> profiles,
        PayProfileContractActivation? activation,
        DateTime now,
        CancellationToken ct)
    {
        var inForce = profiles.FirstOrDefault(p => p.IsActive);
        var previousSalary = inForce?.BaseSalary;
        var step = EmployeePayProfileVersioning.CreateNextVersion(
            request.EmployeeId,
            profiles,
            new PayProfileVersionChange(
                request.EffectiveFrom,
                request.ProposedBaseSalary,
                request.ProposedPayGradeId,
                // Over a profile in force the new version is in force. A contract's first activation says so itself (PAY-029).
                IsActive: inForce is not null || activation is null || activation.FirstVersionIsActive,
                HistoryReason: activation?.HistoryReason ?? request.Reason));
        if (step.History is not null)
            await repository.AddAsync(step.History, ct);
        var next = step.Next;
        await repository.AddAsync(next, ct);

        request.State = PayProfileChangeState.Applied;
        request.AppliedAt = now;
        request.AppliedProfileId = next.Id;
        request.LastApplyError = null;

        var gradeCode = next.PayGradeId is Guid gradeId
            ? await repository.PayGrades.AsNoTracking().Where(g => g.Id == gradeId).Select(g => g.GradeCode).FirstOrDefaultAsync(ct)
            : null;

        return new PayProfileChangeAppliedEvent(
            request.Id, request.EmployeeId, next.Id, step.Previous?.Id, request.EffectiveFrom, request.Source.ToString(), request.SourceReference,
            gradeCode, next.BaseSalary, previousSalary, now);
    }

    private static PayProfileChangeResult Refuse(PayProfileChangeRequest request, PayProfileChangeFailure failure, string error) =>
        PayProfileChangeResult.Fail(failure, error, request.Id, request.State);
}

/// <summary>The events of an applier that has no bus (<see cref="PayProfileChangeApplier.Detached"/>): nothing is staged.</summary>
internal sealed class NoPayProfileChangeEvents : IPayProfileChangeEventPublisher
{
    public static readonly NoPayProfileChangeEvents Instance = new();

    public Task PublishSubmittedAsync(PayProfileChangeSubmittedEvent message, CancellationToken ct) => Task.CompletedTask;

    public Task PublishDecidedAsync(PayProfileChangeDecidedEvent message, CancellationToken ct) => Task.CompletedTask;

    public Task PublishAppliedAsync(PayProfileChangeAppliedEvent message, CancellationToken ct) => Task.CompletedTask;
}
