using Payroll.Domain.Entities;

namespace Payroll.Application.Services;

/// <summary>Builds FRD-001 scan inputs from persisted payroll run data.</summary>
public static class FraudScanInputBuilder
{
    public static IReadOnlyList<SalaryLineScanInput> FromRun(
        PayrollRun run,
        IReadOnlyList<SalaryLine> lines,
        IReadOnlyDictionary<Guid, EmployeePayProfile> profilesByEmployee,
        IReadOnlySet<Guid> employeesWithInputSnapshot,
        DateTime asOfUtc,
        IReadOnlyDictionary<Guid, string>? pinnedAccountNumbers = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(lines);

        return lines.Select(line =>
        {
            profilesByEmployee.TryGetValue(line.EmployeeId, out var profile);
            var hasSnapshot = employeesWithInputSnapshot.Contains(line.EmployeeId);
            return new SalaryLineScanInput(
                line.EmployeeId,
                line.NetPay,
                // PLAN-55 (D-PAY-15): FR-04 compares the accounts this run pays into: each line's pinned account, by its id.
                Iban: line.BankAccountId is Guid accountId && pinnedAccountNumbers is not null
                      && pinnedAccountNumbers.TryGetValue(accountId, out var number)
                    ? number
                    : null,
                HasAttendance: hasSnapshot ? true : false,
                HasSourceRecord: hasSnapshot ? true : false,
                HireDate: profile?.EffectiveFrom,
                NationalIdHash: null);
        }).ToList();
    }
}
