using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Services;

namespace Payroll.Application.Features.PayrollConfigurations;

/// <summary>PLAN-54 (§3): a second person, who is not an editor of the version, approves a draft. Approval fixes the content.</summary>
public sealed record ApprovePayrollFrameworkCommand(Guid FrameworkId, Guid ActorId) : IRequest<PayrollFrameworkResult>;

/// <summary>PLAN-54 (§3): a third person, neither an editor nor the approver, attests an approved version legally.</summary>
public sealed record AttestPayrollFrameworkCommand(Guid FrameworkId, Guid ActorId) : IRequest<PayrollFrameworkResult>;

/// <summary>PLAN-54 (§3): the approver or the attestor publishes an approved, attested version from its own effective date.</summary>
public sealed record PublishPayrollFrameworkCommand(Guid FrameworkId, Guid ActorId, string? ImpactSummary) : IRequest<PayrollFrameworkResult>;

/// <summary>PLAN-54 (§3, rule 3): makes a published version the active one of its code. It never publishes.</summary>
public sealed record ActivatePayrollFrameworkCommand(Guid FrameworkId, Guid ActorId) : IRequest<PayrollFrameworkResult>;

/// <summary>
/// The interim gate's clock and catalog, as the cycle's handlers and the calculation use them: an exception is active on the payroll
/// calendar's date (Damascus), never on the UTC date.
/// </summary>
public static class PayrollInterimGate
{
    public const string GuardMissing = "تعذّر التحقق من الاستثناءات القانونية: كتالوج الضوابط القانونية غير متاح لهذه الخدمة";

    /// <summary>
    /// Whether an exception is active today. With no catalog to ask (a host that registers none), no exception is active: the gate
    /// fails closed, so an interim value is never approved, attested or published on a guess.
    /// </summary>
    public static Func<string, bool> Today(IPayrollLegalControlGuard? guard, IPayrollCalendar calendar)
    {
        if (guard is null)
            return _ => false;
        var today = calendar.Today;
        return exceptionId => guard.IsExceptionActive(exceptionId, today);
    }

    /// <summary>
    /// The start of a calculation with a pinned or resolved version: every exception the version cites must be active today.
    /// Returns the Arabic refusal («انتهى الاستثناء القانوني X»), or null. A legacy version cites nothing and always passes. With no
    /// catalog to ask, a version that holds interim values is refused: the gate fails closed.
    /// </summary>
    public static string? RefusalForRun(PayrollConfiguration config, IPayrollLegalControlGuard? guard, IPayrollCalendar calendar)
    {
        var cited = PayrollFrameworkGovernance.CitedExceptionIds(config);
        if (cited.Count == 0)
            return null;
        if (guard is null)
            return GuardMissing;

        var today = calendar.Today;
        var expired = cited.Where(id => !guard.IsExceptionActive(id, today)).ToList();
        return expired.Count == 0
            ? null
            : string.Join("؛ ", expired.Select(PayrollFrameworkGovernance.ExceptionExpired));
    }

    /// <summary>The line a run's job log and its details carry when the pinned version holds interim values.</summary>
    public static string? InterimIndicator(PayrollConfiguration config)
    {
        if (!PayrollFrameworkGovernance.HasInterimValues(config))
            return null;
        var cited = PayrollFrameworkGovernance.CitedExceptionIds(config);
        return cited.Count == 0 ? "إطار مؤقت" : "إطار مؤقت — استثناء " + string.Join("، ", cited);
    }
}

public sealed class ApprovePayrollFrameworkHandler(IPayrollRepository repository, IPayrollCalendar calendar, IPayrollLegalControlGuard? guard = null)
    : IRequestHandler<ApprovePayrollFrameworkCommand, PayrollFrameworkResult>
{
    public async Task<PayrollFrameworkResult> Handle(ApprovePayrollFrameworkCommand request, CancellationToken cancellationToken)
    {
        var version = await repository.GetFrameworkForUpdateAsync(request.FrameworkId, cancellationToken);
        if (version is null)
            return PayrollFrameworkResult.NotFound();

        var (ok, error) = PayrollFrameworkGovernance.TryApprove(version, request.ActorId, PayrollInterimGate.Today(guard, calendar), calendar.UtcNow);
        if (!ok)
        {
            repository.ClearChangeTracker();
            return PayrollFrameworkResult.Invalid(error!, version);
        }

        version.UpdatedAt = calendar.UtcNow;
        return await PayrollFrameworkQueries.SaveAsync(repository, cancellationToken) ?? PayrollFrameworkResult.Ok(version);
    }
}

public sealed class AttestPayrollFrameworkHandler(IPayrollRepository repository, IPayrollCalendar calendar, IPayrollLegalControlGuard? guard = null)
    : IRequestHandler<AttestPayrollFrameworkCommand, PayrollFrameworkResult>
{
    public async Task<PayrollFrameworkResult> Handle(AttestPayrollFrameworkCommand request, CancellationToken cancellationToken)
    {
        var version = await repository.GetFrameworkForUpdateAsync(request.FrameworkId, cancellationToken);
        if (version is null)
            return PayrollFrameworkResult.NotFound();

        var cited = PayrollFrameworkGovernance.CitedDecisionIds(version);
        var registered = cited.Count == 0
            ? []
            : await repository.LegalDecisionRecords.AsNoTracking()
                .Where(d => cited.Contains(d.DecisionId))
                .Select(d => d.DecisionId)
                .ToListAsync(cancellationToken);

        var (ok, error, attestation) = PayrollFrameworkGovernance.TryAttest(
            version, request.ActorId, PayrollInterimGate.Today(guard, calendar), registered, calendar.UtcNow);
        if (!ok)
        {
            repository.ClearChangeTracker();
            return PayrollFrameworkResult.Invalid(error!, version);
        }

        // One active attestation per version: an earlier one (another attestor's) is voided, never deleted.
        var earlier = await repository.LegalAttestations
            .Where(a => a.PayrollConfigurationId == version.Id && a.Status == "Active")
            .ToListAsync(cancellationToken);
        foreach (var previous in earlier)
            previous.Status = "Voided";
        await repository.AddAsync(attestation!, cancellationToken);
        // The attestation is of this row version: an attestation never lands on a version that changed meanwhile.
        version.UpdatedAt = calendar.UtcNow;
        return await PayrollFrameworkQueries.SaveAsync(repository, cancellationToken) ?? PayrollFrameworkResult.Ok(version);
    }
}

public sealed class PublishPayrollFrameworkHandler(IPayrollRepository repository, IPayrollCalendar calendar, IPayrollLegalControlGuard? guard = null)
    : IRequestHandler<PublishPayrollFrameworkCommand, PayrollFrameworkResult>
{
    public async Task<PayrollFrameworkResult> Handle(PublishPayrollFrameworkCommand request, CancellationToken cancellationToken)
    {
        var version = await repository.GetFrameworkForUpdateAsync(request.FrameworkId, cancellationToken);
        if (version is null)
            return PayrollFrameworkResult.NotFound();

        var sameCode = await repository.PayrollConfigurations
            .Where(c => c.Code == version.Code && c.Id != version.Id)
            .ToListAsync(cancellationToken);
        sameCode.Add(version);
        var attestation = await repository.LegalAttestations.AsNoTracking()
            .Where(a => a.PayrollConfigurationId == version.Id && a.Status == "Active")
            .OrderByDescending(a => a.AttestedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var latestPosted = await PayrollFrameworkQueries.LatestPostedPeriodEndAsync(repository, version.RegimeCode, cancellationToken);

        var (ok, error, notice) = PayrollFrameworkGovernance.TryPublish(
            version, sameCode, request.ActorId, attestation, PayrollInterimGate.Today(guard, calendar), latestPosted, request.ImpactSummary);
        if (!ok)
        {
            repository.ClearChangeTracker();
            return PayrollFrameworkResult.Invalid(error!, version);
        }

        version.PublishedAt = calendar.UtcNow;
        version.UpdatedAt = calendar.UtcNow;
        notice!.PublishedAt = calendar.UtcNow;
        await repository.AddAsync(notice, cancellationToken);
        return await PayrollFrameworkQueries.SaveAsync(repository, cancellationToken) ?? PayrollFrameworkResult.Ok(version);
    }
}

public sealed class ActivatePayrollFrameworkHandler(IPayrollRepository repository)
    : IRequestHandler<ActivatePayrollFrameworkCommand, PayrollFrameworkResult>
{
    public async Task<PayrollFrameworkResult> Handle(ActivatePayrollFrameworkCommand request, CancellationToken cancellationToken)
    {
        var sameCode = await repository.PayrollConfigurations
            .Where(c => repository.PayrollConfigurations.Any(x => x.Id == request.FrameworkId && x.Code == c.Code))
            .ToListAsync(cancellationToken);
        var version = sameCode.FirstOrDefault(c => c.Id == request.FrameworkId);
        if (version is null)
            return PayrollFrameworkResult.NotFound();

        var (ok, error) = PayrollConfigurationVersioning.TryActivate(version, sameCode);
        if (!ok)
        {
            repository.ClearChangeTracker();
            return PayrollFrameworkResult.Invalid(error!, version);
        }

        return await PayrollFrameworkQueries.SaveAsync(repository, cancellationToken) ?? PayrollFrameworkResult.Ok(version);
    }
}
