using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Application.Services;

/// <summary>
/// PLAN-58 (§6, P-10): what the fraud rules read, from the system's own records rather than placeholders.
/// </summary>
/// <param name="EmployeesWithInputSnapshot">The employees the run's calculation took a snapshot of (a regular line's source record).</param>
/// <param name="PinnedAccountNumbers">FR-04: the numbers of the accounts the lines pinned (PLAN-55), by account id.</param>
/// <param name="HireDates">FR-07: the earliest <c>EffectiveFrom</c> of each employee's pay profiles.</param>
/// <param name="AttendanceByEmployee">FR-01: from PLAN-57's receipts of the run's period: whether the employee worked a day of it; absent
/// when there is no receipt (no alert: a missing receipt is the attendance gate's).</param>
/// <param name="ExpectedNetPay">FR-05: the employee's net in the last posted regular run before this one.</param>
public sealed record FraudScanFacts(
    IReadOnlySet<Guid> EmployeesWithInputSnapshot,
    IReadOnlyDictionary<Guid, string>? PinnedAccountNumbers = null,
    IReadOnlyDictionary<Guid, DateTime>? HireDates = null,
    IReadOnlyDictionary<Guid, bool>? AttendanceByEmployee = null,
    IReadOnlyDictionary<Guid, decimal>? ExpectedNetPay = null);

/// <summary>Builds FRD-001 scan inputs from persisted payroll run data.</summary>
public static class FraudScanInputBuilder
{
    public static IReadOnlyList<SalaryLineScanInput> FromRun(
        PayrollRun run,
        IReadOnlyList<SalaryLine> lines,
        FraudScanFacts facts,
        DateTime asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(facts);
        _ = asOfUtc;

        // FR-08: a correction line's source is the run it corrects (SourceRunId); a regular line's is its calculation's snapshot.
        var isCorrection = run.RunType == PayrollRunType.Correction && run.SourceRunId.HasValue;
        return lines.Select(line => new SalaryLineScanInput(
            line.EmployeeId,
            line.NetPay,
            // PLAN-55 (D-PAY-15): FR-04 compares the accounts this run pays into: each line's pinned account, by its id.
            Iban: line.BankAccountId is Guid accountId && facts.PinnedAccountNumbers is not null
                  && facts.PinnedAccountNumbers.TryGetValue(accountId, out var number)
                ? number
                : null,
            HasAttendance: facts.AttendanceByEmployee is not null && facts.AttendanceByEmployee.TryGetValue(line.EmployeeId, out var present)
                ? present
                : null,
            HireDate: facts.HireDates is not null && facts.HireDates.TryGetValue(line.EmployeeId, out var hired) ? hired : null,
            HasSourceRecord: isCorrection || facts.EmployeesWithInputSnapshot.Contains(line.EmployeeId),
            NationalIdHash: null,
            ExpectedNetPay: facts.ExpectedNetPay is not null && facts.ExpectedNetPay.TryGetValue(line.EmployeeId, out var expected) ? expected : null))
            .ToList();
    }
}
