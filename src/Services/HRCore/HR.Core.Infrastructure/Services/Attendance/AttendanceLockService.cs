using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Attendance;

/// <summary>
/// PLAN-57 (§1, rule 9): the lock of an attendance day. Day (E, D) is locked when employee E has a summary in a period that covers D.
/// The period's own status plays no part. Each answer is one query.
/// </summary>
public sealed class AttendanceLockService(HRCoreDbContext db)
{
    public Task<bool> IsLockedAsync(Guid employeeId, DateOnly date, CancellationToken ct = default) =>
        db.AttendanceEmployeeSummaries.AsNoTracking()
            .AnyAsync(s => s.EmployeeId == employeeId && s.Period!.PeriodStart <= date && s.Period.PeriodEnd >= date, ct);

    /// <summary>The locked days of several employees within [from, to], in one query.</summary>
    public async Task<AttendanceLockedDays> GetLockedDaysAsync(
        IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return AttendanceLockedDays.None;

        var ids = employeeIds as List<Guid> ?? employeeIds.ToList();
        var rows = await db.AttendanceEmployeeSummaries.AsNoTracking()
            .Where(s => ids.Contains(s.EmployeeId) && s.Period!.PeriodStart <= to && s.Period.PeriodEnd >= from)
            .Select(s => new { s.EmployeeId, s.Period!.PeriodStart, s.Period.PeriodEnd })
            .ToListAsync(ct);

        return new AttendanceLockedDays(rows
            .GroupBy(r => r.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(r => (r.PeriodStart, r.PeriodEnd)).ToList()));
    }
}

/// <summary>The closed periods of a set of employees: answers whether one day of one employee is locked.</summary>
public sealed class AttendanceLockedDays(Dictionary<Guid, List<(DateOnly Start, DateOnly End)>> closedPeriods)
{
    public static AttendanceLockedDays None { get; } = new([]);

    public bool IsLocked(Guid employeeId, DateOnly date) =>
        closedPeriods.TryGetValue(employeeId, out var periods) && periods.Exists(p => p.Start <= date && date <= p.End);

    public bool HasAnyFor(Guid employeeId) => closedPeriods.ContainsKey(employeeId);
}
