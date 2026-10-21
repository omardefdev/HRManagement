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
        DateTime asOfUtc)
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
                Iban: profile?.BankAccountIban,
                HasAttendance: hasSnapshot ? true : false,
                HasSourceRecord: hasSnapshot ? true : false,
                HireDate: profile?.EffectiveFrom,
                NationalIdHash: null);
        }).ToList();
    }
}
