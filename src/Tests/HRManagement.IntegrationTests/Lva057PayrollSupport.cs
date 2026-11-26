using HRManagement.MessageContracts.Events;
using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Entities;
using Payroll.Infrastructure.Consumers;
using Payroll.Infrastructure.Data;

namespace HRManagement.IntegrationTests;

/// <summary>PLAN-57: one version of an attendance summary, as HR.Core publishes it (the tests' message).</summary>
internal sealed record Lva057Summary(
    Guid SummaryId,
    int Version,
    Guid EmployeeId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int AbsentDays,
    string Reason = "Close",
    Guid? TenantId = null) : IAttendanceSummaryPublished
{
    public Guid AttendancePeriodId { get; init; } = Guid.Parse("57000000-0000-4000-8000-0000000000aa");

    public int WorkingDays { get; init; } = 22;

    public int PresentDays => WorkingDays - AbsentDays;

    public int OnLeaveDays => 0;

    public int RemoteDays => 0;

    public int UnrecordedWorkingDays => 0;

    public Guid? CorrectionId => Version > 1 ? Guid.NewGuid() : null;

    public DateTime OccurredAt { get; init; } = new(2028, 2, 1, 8, 0, 0, DateTimeKind.Utc);

    public string? CorrelationId => null;

    public string SourceSystem => "HR.Core";

    /// <summary>The summary of an employee's month, version 1 (the close).</summary>
    public static Lva057Summary Close(Guid employee, int year, int month, int absentDays, Guid? summaryId = null, Guid? tenant = null)
    {
        var start = new DateOnly(year, month, 1);
        return new Lva057Summary(summaryId ?? Guid.NewGuid(), 1, employee, start, start.AddMonths(1).AddDays(-1), absentDays, "Close", tenant);
    }

    /// <summary>The next version of the same summary, with its absolute absence.</summary>
    public Lva057Summary Next(int absentDays, int? version = null, string reason = "Correction") =>
        this with { Version = version ?? Version + 1, AbsentDays = absentDays, Reason = reason };
}

internal static class Lva057Payroll
{
    public static Task<AttendanceHandoffOutcome> ReceiveAsync(PayrollDbContext db, IAttendanceSummaryPublished message)
    {
        db.ChangeTracker.Clear();
        return new AttendanceSummaryHandoff(db).ApplyAsync(message);
    }

    public static async Task<AttendanceSummaryReceipt?> ReceiptAsync(PayrollDbContext db, Guid summaryId)
    {
        db.ChangeTracker.Clear();
        return await db.AttendanceSummaryReceipts.AsNoTracking().SingleOrDefaultAsync(r => r.SummaryId == summaryId);
    }

    /// <summary>The difference rows of a summary, by version.</summary>
    public static async Task<List<AbsenceDeduction>> RowsAsync(PayrollDbContext db, Guid summaryId)
    {
        db.ChangeTracker.Clear();
        var prefix = $"ATT:{summaryId:D}:";
        return await db.AbsenceDeductions.AsNoTracking()
            .Where(a => a.SourceReference != null && a.SourceReference.StartsWith(prefix))
            .OrderBy(a => a.SourceVersion)
            .ToListAsync();
    }
}
