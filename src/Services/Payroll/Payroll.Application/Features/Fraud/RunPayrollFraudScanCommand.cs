using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Services;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Services;

namespace Payroll.Application.Features.Fraud;

public record RunPayrollFraudScanCommand(Guid PayrollRunId, bool Persist = true)
    : IRequest<RunPayrollFraudScanResult>;

public record RunPayrollFraudScanResult(
    bool Success,
    string? Error,
    int AlertsCreated,
    int HoldsCreated,
    IReadOnlyList<Guid> HoldIds);

public class RunPayrollFraudScanHandler(
    IPayrollRepository repository,
    FraudAnomalyEngine fraudEngine)
    : IRequestHandler<RunPayrollFraudScanCommand, RunPayrollFraudScanResult>
{
    public async Task<RunPayrollFraudScanResult> Handle(
        RunPayrollFraudScanCommand request,
        CancellationToken cancellationToken)
    {
        var run = await repository.GetPayrollRunAsync(request.PayrollRunId, includeLines: true, cancellationToken);
        if (run is null)
            return new RunPayrollFraudScanResult(false, "التشغيل غير موجود.", 0, 0, []);

        var lines = run.SalaryLines.ToList();
        if (lines.Count == 0)
            lines = await repository.SalaryLines
                .Where(l => l.PayrollRunId == run.Id)
                .ToListAsync(cancellationToken);

        if (lines.Count == 0)
            return new RunPayrollFraudScanResult(false, "لا توجد بنود للفحص.", 0, 0, []);

        var employeeIds = lines.Select(l => l.EmployeeId).Distinct().ToList();
        var releasedForRun = await repository.PaymentHolds
            .AsNoTracking()
            // A person released a fraud hold of this run: the scan does not raise it again. (A service-regime or account hold's release
            // says nothing about fraud.)
            .Where(h => h.PayrollRunId == run.Id && h.Status == PaymentHoldStatus.Released && h.HoldType == PaymentHoldType.Fraud)
            .Select(h => h.EmployeeId)
            .ToHashSetAsync(cancellationToken);

        var job = await repository.PayrollRunJobs
            .AsNoTracking()
            .Include(j => j.InputSnapshots)
            .Where(j => j.PayrollRunId == run.Id)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var snapshotEmployees = job?.InputSnapshots.Select(s => s.EmployeeId).ToHashSet()
                              ?? new HashSet<Guid>();

        // PLAN-55 (D-PAY-15): FR-04 (one account shared by several employees) reads the accounts the lines pinned.
        var pinned = await Features.BankAccounts.BankAccountPinning.PinnedAccountsAsync(repository.EmployeeBankAccounts, lines, cancellationToken);
        var pinnedNumbers = pinned.ToDictionary(p => p.Key, p => p.Value.AccountNumber);

        // PLAN-58 (§6): FR-07 the earliest date of the employee's pay profiles (his first in this service), not the active version's.
        var hireDates = (await repository.EmployeePayProfiles.AsNoTracking()
                .Where(p => employeeIds.Contains(p.EmployeeId))
                .GroupBy(p => p.EmployeeId)
                .Select(g => new { EmployeeId = g.Key, First = g.Min(p => p.EffectiveFrom) })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.EmployeeId, x => x.First);

        // PLAN-58 (§6): FR-01 from PLAN-57's receipts of the run's period. A receipt that counts as many absent days as the period has
        // working days is «no attendance»; no receipt is no fact (the attendance gate handles a missing receipt).
        var periodStart = DateOnly.FromDateTime(run.PeriodStart);
        var periodEnd = DateOnly.FromDateTime(run.PeriodEnd);
        var receipts = await repository.AttendanceSummaryReceipts.AsNoTracking()
            .Where(r => employeeIds.Contains(r.EmployeeId) && r.PeriodStart <= periodEnd && r.PeriodEnd >= periodStart)
            .Select(r => new { r.EmployeeId, r.PeriodStart, r.PeriodEnd, r.LatestAbsentDays })
            .ToListAsync(cancellationToken);
        var config = run.PayrollConfigurationId is Guid configId
            ? await repository.PayrollConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == configId, cancellationToken)
            : null;
        var attendance = receipts
            .GroupBy(r => r.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => g.Any(r => r.LatestAbsentDays < PayrollPeriodRules.CountWorkingDays(r.PeriodStart, r.PeriodEnd, config, null)));

        // PLAN-58 (§6): FR-05 the employee's net in the last posted regular run before this one.
        var previous = await (from line in repository.SalaryLines.AsNoTracking()
                              join earlier in repository.PayrollRuns.AsNoTracking() on line.PayrollRunId equals earlier.Id
                              where employeeIds.Contains(line.EmployeeId)
                                    && earlier.Status == Domain.Enums.PayrollRunStatus.Posted
                                    && earlier.RunType == Domain.Enums.PayrollRunType.Regular
                                    && earlier.PeriodStart < run.PeriodStart
                                    && earlier.Id != run.Id
                              select new { line.EmployeeId, earlier.PeriodStart, line.NetPay })
            .ToListAsync(cancellationToken);
        var expectedNet = previous
            .GroupBy(p => p.EmployeeId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.PeriodStart).First().NetPay);

        var asOf = run.RunDate;
        var scanInputs = FraudScanInputBuilder.FromRun(
            run, lines, new FraudScanFacts(snapshotEmployees, pinnedNumbers, hireDates, attendance, expectedNet), asOf);

        var scan = fraudEngine.Scan(run.Id, scanInputs, asOfUtc: asOf);
        if (!request.Persist)
            return new RunPayrollFraudScanResult(
                true,
                null,
                scan.Alerts.Count,
                scan.Holds.Count,
                scan.Holds.Select(h => h.Id).ToList());

        var alertsCreated = 0;
        var holdsCreated = 0;
        var holdIds = new List<Guid>();

        foreach (var alert in scan.Alerts)
        {
            if (releasedForRun.Contains(alert.EmployeeId))
                continue;

            var duplicate = await repository.FraudAlerts.AnyAsync(
                a => a.PayrollRunId == run.Id
                     && a.EmployeeId == alert.EmployeeId
                     && a.RuleId == alert.RuleId
                     && a.Status == FraudAlertStatus.Open,
                cancellationToken);
            if (duplicate)
                continue;

            await repository.AddAsync(alert, cancellationToken);
            alertsCreated++;
        }

        foreach (var hold in scan.Holds)
        {
            if (releasedForRun.Contains(hold.EmployeeId))
                continue;

            var duplicate = await repository.PaymentHolds.AnyAsync(
                h => h.EmployeeId == hold.EmployeeId
                     && h.Status == PaymentHoldStatus.Active
                     && (h.PayrollRunId == null || h.PayrollRunId == run.Id),
                cancellationToken);
            if (duplicate)
                continue;

            hold.PayrollRunId ??= run.Id;
            await repository.AddAsync(hold, cancellationToken);
            holdsCreated++;
            holdIds.Add(hold.Id);
        }

        if (alertsCreated > 0 || holdsCreated > 0)
            await repository.SaveChangesAsync(cancellationToken);

        return new RunPayrollFraudScanResult(true, null, alertsCreated, holdsCreated, holdIds);
    }
}
