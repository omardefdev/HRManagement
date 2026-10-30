using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Legal;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Legal;

/// <summary>SYR-007: exception queue for unclassified service regimes.</summary>
public sealed class ServiceRegimeExceptionService(
    HRCoreDbContext db,
    ServiceRegimeTransitionService transitionService)
{
    public async Task<ServiceRegimeException?> EnqueueIfUnclassifiedAsync(
        Employee employee,
        EmploymentRecord? employmentRecord,
        ServiceRegimeExceptionSource sourceContext,
        string? legalSourceId,
        string? attemptedRegimeId = null,
        CancellationToken ct = default)
    {
        var regimeId = employmentRecord?.LegalRegimeId;
        if (!ServiceRegimeExceptionRules.ShouldEnqueueOnHire(regimeId))
            return null;

        var existing = await db.ServiceRegimeExceptions
            .FirstOrDefaultAsync(e =>
                e.EmployeeId == employee.Id
                && e.ExceptionType == ServiceRegimeExceptionType.UnclassifiedRegime
                && e.Status == ServiceRegimeExceptionStatus.Open, ct);

        if (existing is not null)
            return existing;

        var reason = sourceContext switch
        {
            ServiceRegimeExceptionSource.Backfill => ServiceRegimeExceptionRules.BuildBackfillReason(),
            ServiceRegimeExceptionSource.Transition => ServiceRegimeExceptionRules.BuildTransitionReason(),
            _ => ServiceRegimeExceptionRules.BuildHireReason(legalSourceId)
        };

        var entry = new ServiceRegimeException
        {
            EmployeeId = employee.Id,
            EmploymentRecordId = employmentRecord?.Id,
            ExceptionType = ServiceRegimeExceptionType.UnclassifiedRegime,
            Status = ServiceRegimeExceptionStatus.Open,
            SourceContext = sourceContext,
            LegalSourceId = legalSourceId,
            AttemptedRegimeId = attemptedRegimeId,
            Reason = reason
        };

        db.ServiceRegimeExceptions.Add(entry);
        await db.SaveChangesAsync(ct);
        return entry;
    }

    public async Task<IReadOnlyList<ServiceRegimeException>> ListOpenAsync(
        ServiceRegimeExceptionType? filterType = null,
        CancellationToken ct = default)
    {
        var query = db.ServiceRegimeExceptions
            .Include(e => e.Employee)
            .Include(e => e.EmploymentRecord)
            .Where(e => e.Status == ServiceRegimeExceptionStatus.Open)
            .AsNoTracking();

        if (filterType is ServiceRegimeExceptionType type)
            query = query.Where(e => e.ExceptionType == type);

        return await query
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<ServiceRegimeException?> EnqueueArticle159Async(
        Guid employeeId,
        Guid? employmentRecordId,
        string fromRegimeId,
        string toRegimeId,
        string conflictingEntitlementsJson,
        ServiceRegimeExceptionSource sourceContext,
        CancellationToken ct = default)
    {
        var existing = await db.ServiceRegimeExceptions
            .FirstOrDefaultAsync(e =>
                e.EmployeeId == employeeId
                && e.ExceptionType == ServiceRegimeExceptionType.Article159Conflict
                && e.Status == ServiceRegimeExceptionStatus.Open, ct);

        if (existing is not null)
            return existing;

        var entry = new ServiceRegimeException
        {
            EmployeeId = employeeId,
            EmploymentRecordId = employmentRecordId,
            ExceptionType = ServiceRegimeExceptionType.Article159Conflict,
            Status = ServiceRegimeExceptionStatus.Open,
            SourceContext = sourceContext,
            AttemptedRegimeId = toRegimeId,
            Reason = ServiceRegimeExceptionRules.BuildArticle159Reason(fromRegimeId, toRegimeId),
            ConflictingEntitlementsJson = conflictingEntitlementsJson
        };

        db.ServiceRegimeExceptions.Add(entry);
        await db.SaveChangesAsync(ct);
        return entry;
    }

    public async Task<string?> ResolveArticle159Async(
        Guid exceptionId,
        string legalDecisionId,
        string resolution,
        Guid? resolvedBy,
        string? conflictNotes = null,
        string? resolvedRegimeId = null,
        string? transitionDecisionRef = null,
        DateTime? effectiveFrom = null,
        string? conditionCode = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(legalDecisionId))
            return ServiceRegimeArticle159Rules.LegalDecisionRequiredMessage;

        if (string.IsNullOrWhiteSpace(resolution))
            return ServiceRegimeExceptionRules.ResolutionRequiredMessage;

        var entry = await db.ServiceRegimeExceptions
            .Include(e => e.EmploymentRecord)
            .FirstOrDefaultAsync(e => e.Id == exceptionId, ct);

        if (entry is null)
            return ServiceRegimeExceptionRules.NotFoundMessage;

        if (entry.ExceptionType != ServiceRegimeExceptionType.Article159Conflict)
            return "هذا الإجراء مخصص لاستثناءات المادة 159.";

        if (entry.Status != ServiceRegimeExceptionStatus.Open)
            return ServiceRegimeExceptionRules.AlreadyResolvedMessage;

        if (!string.IsNullOrWhiteSpace(resolvedRegimeId))
        {
            var transitionError = await transitionService.ApplyTransitionAsync(
                entry.EmployeeId,
                resolvedRegimeId,
                transitionDecisionRef ?? legalDecisionId,
                conditionCode,
                effectiveFrom ?? DateTime.UtcNow,
                resolvedBy,
                entry.EmploymentRecordId,
                conflictNotes,
                ct);

            if (transitionError is not null)
                return transitionError;
        }

        entry.Status = ServiceRegimeExceptionStatus.Resolved;
        entry.LegalDecisionId = legalDecisionId.Trim();
        entry.ConflictNotes = string.IsNullOrWhiteSpace(conflictNotes) ? null : conflictNotes.Trim();
        entry.ResolvedRegimeId = resolvedRegimeId;
        entry.Resolution = resolution.Trim();
        entry.ResolvedBy = resolvedBy;
        entry.ResolvedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> ResolveAsync(
        Guid exceptionId,
        string resolvedRegimeId,
        string transitionDecisionRef,
        DateTime effectiveFrom,
        string? conditionCode,
        string resolution,
        Guid? resolvedBy,
        string? conflictNotes = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(resolvedRegimeId))
            return ServiceRegimeExceptionRules.RegimeRequiredOnResolveMessage;

        if (string.IsNullOrWhiteSpace(resolution))
            return ServiceRegimeExceptionRules.ResolutionRequiredMessage;

        var entry = await db.ServiceRegimeExceptions
            .Include(e => e.EmploymentRecord)
            .FirstOrDefaultAsync(e => e.Id == exceptionId, ct);

        if (entry is null)
            return ServiceRegimeExceptionRules.NotFoundMessage;

        if (entry.Status != ServiceRegimeExceptionStatus.Open)
            return ServiceRegimeExceptionRules.AlreadyResolvedMessage;

        var transitionError = await transitionService.ApplyTransitionAsync(
            entry.EmployeeId,
            resolvedRegimeId,
            transitionDecisionRef,
            conditionCode,
            effectiveFrom,
            resolvedBy,
            entry.EmploymentRecordId,
            conflictNotes,
            ct);

        if (transitionError is not null)
            return transitionError;

        entry.Status = ServiceRegimeExceptionStatus.Resolved;
        entry.ResolvedRegimeId = resolvedRegimeId;
        entry.Resolution = resolution.Trim();
        entry.ResolvedBy = resolvedBy;
        entry.ResolvedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(conflictNotes))
            entry.ConflictNotes = conflictNotes.Trim();

        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> DismissAsync(
        Guid exceptionId,
        string resolution,
        Guid? resolvedBy,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(resolution))
            return ServiceRegimeExceptionRules.ResolutionRequiredMessage;

        var entry = await db.ServiceRegimeExceptions.FindAsync([exceptionId], ct);
        if (entry is null)
            return ServiceRegimeExceptionRules.NotFoundMessage;

        if (entry.Status != ServiceRegimeExceptionStatus.Open)
            return ServiceRegimeExceptionRules.AlreadyResolvedMessage;

        entry.Status = ServiceRegimeExceptionStatus.Dismissed;
        entry.Resolution = resolution.Trim();
        entry.ResolvedBy = resolvedBy;
        entry.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<bool> HasOpenExceptionAsync(Guid employeeId, CancellationToken ct = default) =>
        await db.ServiceRegimeExceptions.AsNoTracking()
            .AnyAsync(e =>
                e.EmployeeId == employeeId
                && e.Status == ServiceRegimeExceptionStatus.Open, ct);
}
