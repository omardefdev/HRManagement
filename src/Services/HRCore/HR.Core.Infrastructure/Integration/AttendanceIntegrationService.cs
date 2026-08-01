using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Enums;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Integration;

public class AttendanceIntegrationService(HRCoreDbContext db, IPublishEndpoint publish)
{
    public async Task UpsertLeaveAttendanceAsync(
        Guid employeeId,
        DateTime startDate,
        DateTime endDate,
        string? notes,
        CancellationToken ct = default)
    {
        var start = DateOnly.FromDateTime(startDate);
        var end = DateOnly.FromDateTime(endDate);
        if (end < start)
            (start, end) = (end, start);

        for (var d = start; d <= end; d = d.AddDays(1))
        {
            var existing = await db.DailyAttendanceRecords
                .FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.AttendanceDate == d, ct);

            if (existing is null)
            {
                db.DailyAttendanceRecords.Add(new DailyAttendanceRecord
                {
                    EmployeeId = employeeId,
                    AttendanceDate = d,
                    Status = DailyAttendanceStatus.OnLeave,
                    Notes = notes
                });
            }
            else
            {
                existing.Status = DailyAttendanceStatus.OnLeave;
                existing.Notes = notes ?? existing.Notes;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveLeaveAttendanceAsync(
        Guid employeeId,
        DateTime startDate,
        DateTime endDate,
        CancellationToken ct = default)
    {
        var start = DateOnly.FromDateTime(startDate);
        var end = DateOnly.FromDateTime(endDate);
        if (end < start)
            (start, end) = (end, start);

        var records = await db.DailyAttendanceRecords
            .Where(x => x.EmployeeId == employeeId
                        && x.AttendanceDate >= start
                        && x.AttendanceDate <= end
                        && x.Status == DailyAttendanceStatus.OnLeave)
            .ToListAsync(ct);

        if (records.Count == 0)
            return;

        db.DailyAttendanceRecords.RemoveRange(records);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> ClosePeriodAndPublishAsync(
        DateOnly periodStart,
        DateOnly periodEnd,
        Guid? orgUnitId,
        CancellationToken ct = default)
    {
        var employeeQuery = db.Employees.AsNoTracking().Where(e => e.CurrentStatus != EmploymentStatus.Terminated);
        if (orgUnitId.HasValue)
            employeeQuery = employeeQuery.Where(e => e.PrimaryOrgUnitId == orgUnitId);

        var employees = await employeeQuery.Select(e => e.Id).ToListAsync(ct);
        var published = 0;

        foreach (var employeeId in employees)
        {
            var records = await db.DailyAttendanceRecords
                .Where(r => r.EmployeeId == employeeId
                            && r.AttendanceDate >= periodStart
                            && r.AttendanceDate <= periodEnd)
                .ToListAsync(ct);

            var summary = new
            {
                PresentDays = records.Count(r => r.Status == DailyAttendanceStatus.Present),
                AbsentDays = records.Count(r => r.Status == DailyAttendanceStatus.Absent),
                OnLeaveDays = records.Count(r => r.Status == DailyAttendanceStatus.OnLeave),
                RemoteDays = records.Count(r => r.Status == DailyAttendanceStatus.Remote)
            };

            await publish.Publish<IAttendancePeriodClosed>(new
            {
                TransactionId = Guid.NewGuid(),
                EmployeeId = employeeId,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                summary.PresentDays,
                summary.AbsentDays,
                summary.OnLeaveDays,
                summary.RemoteDays,
                OccurredAt = DateTime.UtcNow,
                CorrelationId = (string?)null,
                SourceSystem = "HR.Core"
            }, ct);

            published++;
        }

        await db.SaveChangesAsync(ct);
        return published;
    }
}
