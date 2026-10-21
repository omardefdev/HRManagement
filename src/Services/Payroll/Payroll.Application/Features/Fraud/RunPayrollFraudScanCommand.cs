using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Services;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;

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
            .Where(h => h.PayrollRunId == run.Id && h.Status == PaymentHoldStatus.Released)
            .Select(h => h.EmployeeId)
            .ToHashSetAsync(cancellationToken);

        var profiles = await repository.EmployeePayProfiles
            .AsNoTracking()
            .Where(p => employeeIds.Contains(p.EmployeeId) && p.IsActive)
            .ToListAsync(cancellationToken);
        var profilesByEmployee = profiles.ToDictionary(p => p.EmployeeId);

        var job = await repository.PayrollRunJobs
            .AsNoTracking()
            .Include(j => j.InputSnapshots)
            .Where(j => j.PayrollRunId == run.Id)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var snapshotEmployees = job?.InputSnapshots.Select(s => s.EmployeeId).ToHashSet()
                              ?? new HashSet<Guid>();

        var asOf = run.RunDate;
        var scanInputs = FraudScanInputBuilder.FromRun(
            run, lines, profilesByEmployee, snapshotEmployees, asOf);

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
