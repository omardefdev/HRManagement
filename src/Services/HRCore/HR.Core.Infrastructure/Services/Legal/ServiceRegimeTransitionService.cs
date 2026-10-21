using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Legal;

/// <summary>SYR-007: applies service regime transitions with append-only history.</summary>
public sealed class ServiceRegimeTransitionService(
    HRCoreDbContext db,
    ServiceRegimeValidator regimeValidator)
{
    public async Task<string?> ApplyTransitionAsync(
        Guid employeeId,
        string toRegimeId,
        string transitionDecisionRef,
        string? conditionCode,
        DateTime effectiveFrom,
        Guid? appliedBy,
        Guid? employmentRecordId = null,
        string? conflictNotes = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(transitionDecisionRef))
            return ServiceRegimeTransitionRules.DecisionRefRequiredMessage;

        var regimeError = await regimeValidator.ValidateAssignableAsync(toRegimeId, ct);
        if (regimeError is not null)
            return regimeError;

        var record = employmentRecordId is Guid recordId
            ? await db.EmploymentRecords.FirstOrDefaultAsync(r => r.Id == recordId && r.EmployeeId == employeeId, ct)
            : await db.EmploymentRecords
                .Where(r => r.EmployeeId == employeeId && r.EndDate == null)
                .OrderByDescending(r => r.StartDate)
                .FirstOrDefaultAsync(ct);

        if (record is null)
            return ServiceRegimeTransitionRules.EmploymentRecordRequiredMessage;

        var fromRegimeId = record.LegalRegimeId;
        if (string.Equals(fromRegimeId, toRegimeId, StringComparison.OrdinalIgnoreCase))
            return ServiceRegimeTransitionRules.SameRegimeMessage;

        if (!string.IsNullOrWhiteSpace(fromRegimeId)
            && !ServiceRegimeTransitionRules.IsAllowed(fromRegimeId, toRegimeId, conditionCode))
            return ServiceRegimeTransitionRules.TransitionNotAllowedMessage;

        db.EmploymentRegimeTransitions.Add(new EmploymentRegimeTransition
        {
            EmployeeId = employeeId,
            EmploymentRecordId = record.Id,
            FromRegimeId = fromRegimeId,
            ToRegimeId = toRegimeId,
            EffectiveFrom = effectiveFrom,
            TransitionDecisionRef = transitionDecisionRef.Trim(),
            ConditionCode = conditionCode,
            ConflictNotes = string.IsNullOrWhiteSpace(conflictNotes) ? null : conflictNotes.Trim(),
            AppliedBy = appliedBy,
            AppliedAt = DateTime.UtcNow
        });

        record.LegalRegimeId = toRegimeId;
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<IReadOnlyList<EmploymentRegimeTransition>> ListForEmployeeAsync(
        Guid employeeId,
        CancellationToken ct = default) =>
        await db.EmploymentRegimeTransitions
            .AsNoTracking()
            .Where(t => t.EmployeeId == employeeId)
            .OrderByDescending(t => t.EffectiveFrom)
            .ThenByDescending(t => t.AppliedAt)
            .ToListAsync(ct);
}
