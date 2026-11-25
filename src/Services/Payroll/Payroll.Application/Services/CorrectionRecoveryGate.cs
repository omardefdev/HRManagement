using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace Payroll.Application.Services;

/// <summary>
/// PLAN-56 (§1, review): when a correction's recovery may be taken back, and whether one already was. A recovery deduction is made
/// with its correction line, at the correction's calculation; but a correction that is only calculated can still be rejected,
/// cancelled or calculated again, and each of those deletes its free recoveries. So a regular run takes a recovery only once its
/// correction is posted: otherwise a later run would deduct a difference that never took effect, and a recalculated correction
/// would make a second recovery of the same difference.
/// </summary>
public static class CorrectionRecoveryGate
{
    private const int ChunkSize = 500;

    /// <summary>
    /// Removes from <paramref name="deductionsByEmployee"/> every free recovery whose correction is not posted (or whose line is
    /// gone). A recovery this run already applied stays: it was taken when its correction was posted.
    /// </summary>
    public static async Task KeepOnlyThoseOfPostedCorrectionsAsync(
        IPayrollRepository repository,
        Dictionary<Guid, List<DeductionRecord>> deductionsByEmployee,
        CancellationToken cancellationToken)
    {
        var free = deductionsByEmployee.Values
            .SelectMany(list => list)
            .Where(d => IsRecovery(d) && d.AppliedInRunId == null)
            .ToList();
        if (free.Count == 0)
            return;

        var lineOf = new Dictionary<Guid, Guid>();
        foreach (var recovery in free)
        {
            if (CorrectionRecovery.TryLineIdOf(recovery.SourceReference, out var lineId))
                lineOf[recovery.Id] = lineId;
        }

        var posted = new HashSet<Guid>();
        foreach (var chunk in lineOf.Values.Distinct().Chunk(ChunkSize))
        {
            var lines = await repository.SalaryLines.AsNoTracking()
                .Where(l => chunk.Contains(l.Id) && l.PayrollRun.Status == PayrollRunStatus.Posted)
                .Select(l => l.Id)
                .ToListAsync(cancellationToken);
            posted.UnionWith(lines);
        }

        var notYet = free
            .Where(d => !lineOf.TryGetValue(d.Id, out var lineId) || !posted.Contains(lineId))
            .Select(d => d.Id)
            .ToHashSet();
        if (notYet.Count == 0)
            return;
        foreach (var employee in deductionsByEmployee.Keys.ToList())
        {
            deductionsByEmployee[employee].RemoveAll(d => notYet.Contains(d.Id));
            if (deductionsByEmployee[employee].Count == 0)
                deductionsByEmployee.Remove(employee);
        }
    }

    /// <summary>True when a later run already took back a recovery of one of <paramref name="run"/>'s lines.</summary>
    public static async Task<bool> HasAppliedRecoveryAsync(IPayrollRepository repository, PayrollRun run, CancellationToken cancellationToken)
    {
        if (run.RunType != PayrollRunType.Correction)
            return false;
        var lines = await repository.SalaryLines.AsNoTracking()
            .Where(l => l.PayrollRunId == run.Id)
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);
        foreach (var chunk in lines.Select(CorrectionRecovery.SourceReferenceOf).Chunk(ChunkSize))
        {
            if (await repository.DeductionRecords.AsNoTracking().AnyAsync(
                    d => d.DeductionType == CorrectionRecovery.DeductionType
                         && d.AppliedInRunId != null
                         && d.SourceReference != null && chunk.Contains(d.SourceReference),
                    cancellationToken))
                return true;
        }

        return false;
    }

    private static bool IsRecovery(DeductionRecord deduction) =>
        string.Equals(deduction.DeductionType, CorrectionRecovery.DeductionType, StringComparison.OrdinalIgnoreCase);
}
