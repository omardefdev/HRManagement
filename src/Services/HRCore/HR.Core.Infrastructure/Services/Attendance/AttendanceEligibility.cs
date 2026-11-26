using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Enums;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Attendance;

/// <summary>An employee a close or a bulk entry covers, with the bounds of his service inside the period.</summary>
public sealed record AttendanceEligibleEmployee(Guid Id, Guid? PrimaryOrgUnitId, DateOnly? HireDate, DateOnly? EndOfService);

/// <summary>
/// PLAN-57 (§1, rule 2): the employees of a close, and of a bulk entry (§2, rule 4):
/// <list type="bullet">
/// <item>those whose primary unit is in the chosen unit's tree (by <c>MaterializedPath</c>, as the org scope reads it), or the whole agency;</item>
/// <item>hired on or before the period's end;</item>
/// <item>in service, or whose service ended on or after the period's first day. The end of service is the last
/// <c>EmploymentStatusHistories.ChangedAt</c> to the status the employee holds, as a date in the application's zone.</item>
/// </list>
/// The plan names <c>Terminated</c>; the same rule is applied to every status that ends the service (retired, resigned, deceased,
/// merged, anonymized), so that such an employee does not need attendance for ever. An employee whose appointment is not complete
/// (<c>PendingActivation</c>) has no attendance yet. An employee whose service ended with no history row for it is left out.
/// </summary>
public sealed class AttendanceEligibility(HRCoreDbContext db)
{
    /// <summary>The statuses that end the service.</summary>
    public static readonly IReadOnlyList<EmploymentStatus> EndedStatuses =
    [
        EmploymentStatus.Terminated, EmploymentStatus.Retired, EmploymentStatus.Resigned,
        EmploymentStatus.Deceased, EmploymentStatus.Merged, EmploymentStatus.Anonymized
    ];

    private static readonly List<string> EndedNames = EndedStatuses.Select(s => s.ToString()).ToList();

    /// <summary>
    /// When a service ended, from the history rows whose new status ends a service: the last row that <b>moved</b> the employee into
    /// such a status. A later row that writes an ended status over an ended one (a leftover leave's cancellation or a medical
    /// decision notes the status it found) is not an end of service: read as one, it would bring a leaver back into the months
    /// after he left. With no moving row (an old record), the earliest row is the end.
    /// </summary>
    public static DateTime? EndOfService(IEnumerable<(string? OldStatus, DateTime ChangedAt)> endedRows)
    {
        var rows = endedRows.ToList();
        if (rows.Count == 0)
            return null;
        var moves = rows.Where(r => string.IsNullOrEmpty(r.OldStatus) || !EndedNames.Contains(r.OldStatus)).ToList();
        return moves.Count > 0 ? moves.Max(r => r.ChangedAt) : rows.Min(r => r.ChangedAt);
    }

    /// <summary>The path prefix of a unit's tree; null when the unit does not exist in the caller's agency.</summary>
    public async Task<string?> UnitTreePathAsync(Guid orgUnitId, CancellationToken ct = default)
    {
        var unit = await db.OrgUnits.AsNoTracking()
            .Where(o => o.Id == orgUnitId)
            .Select(o => new { o.Id, o.MaterializedPath })
            .FirstOrDefaultAsync(ct);
        if (unit is null)
            return null;
        return string.IsNullOrEmpty(unit.MaterializedPath) ? $"/{unit.Id:D}/" : unit.MaterializedPath;
    }

    public async Task<List<AttendanceEligibleEmployee>> GetAsync(
        Guid? orgUnitId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default)
    {
        var query = db.Employees.AsNoTracking().Where(e => e.CurrentStatus != EmploymentStatus.PendingActivation);

        if (orgUnitId is Guid unitId)
        {
            var path = await UnitTreePathAsync(unitId, ct);
            if (path is null)
                return [];
            var treeUnits = db.OrgUnits.Where(o => o.MaterializedPath.StartsWith(path) || o.Id == unitId).Select(o => o.Id);
            query = query.Where(e => e.PrimaryOrgUnitId != null && treeUnits.Contains(e.PrimaryOrgUnitId.Value));
        }

        // A hire date is a date stored at midnight UTC.
        var hiredBefore = periodEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rows = await query
            .Where(e => e.HireDate == null || e.HireDate < hiredBefore)
            .OrderBy(e => e.Id)
            .Select(e => new { e.Id, e.PrimaryOrgUnitId, e.HireDate, e.CurrentStatus })
            .ToListAsync(ct);

        var endedIds = rows.Where(r => EndedStatuses.Contains(r.CurrentStatus)).Select(r => r.Id).ToList();
        var endOfService = new Dictionary<Guid, DateTime>();
        if (endedIds.Count > 0)
        {
            var history = await db.EmploymentStatusHistories.AsNoTracking()
                .Where(h => endedIds.Contains(h.EmployeeId) && EndedNames.Contains(h.NewStatus))
                .Select(h => new { h.EmployeeId, h.OldStatus, h.ChangedAt })
                .ToListAsync(ct);
            foreach (var employee in history.GroupBy(h => h.EmployeeId))
            {
                if (EndOfService(employee.Select(h => (h.OldStatus, h.ChangedAt))) is { } at)
                    endOfService[employee.Key] = at;
            }
        }

        var result = new List<AttendanceEligibleEmployee>(rows.Count);
        foreach (var row in rows)
        {
            DateOnly? ended = null;
            if (EndedStatuses.Contains(row.CurrentStatus))
            {
                if (!endOfService.TryGetValue(row.Id, out var last))
                    continue;
                ended = AttendanceCalendar.LocalDate(last);
                if (ended < periodStart)
                    continue;
            }

            result.Add(new AttendanceEligibleEmployee(
                row.Id,
                row.PrimaryOrgUnitId,
                row.HireDate is { } hired ? DateOnly.FromDateTime(hired) : null,
                ended));
        }

        return result;
    }

    /// <summary>One employee's bounds, for a correction's recalculation: the same rule, whatever his unit.</summary>
    public async Task<AttendanceEligibleEmployee?> GetOneAsync(Guid employeeId, CancellationToken ct = default)
    {
        var row = await db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => new { e.Id, e.PrimaryOrgUnitId, e.HireDate, e.CurrentStatus })
            .FirstOrDefaultAsync(ct);
        if (row is null)
            return null;

        DateOnly? ended = null;
        if (EndedStatuses.Contains(row.CurrentStatus))
        {
            var history = await db.EmploymentStatusHistories.AsNoTracking()
                .Where(h => h.EmployeeId == employeeId && EndedNames.Contains(h.NewStatus))
                .Select(h => new { h.OldStatus, h.ChangedAt })
                .ToListAsync(ct);
            if (EndOfService(history.Select(h => (h.OldStatus, h.ChangedAt))) is { } changed)
                ended = AttendanceCalendar.LocalDate(changed);
        }

        return new AttendanceEligibleEmployee(
            row.Id, row.PrimaryOrgUnitId, row.HireDate is { } hired ? DateOnly.FromDateTime(hired) : null, ended);
    }
}
