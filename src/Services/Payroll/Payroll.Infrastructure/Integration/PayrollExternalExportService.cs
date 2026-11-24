using HRManagement.Integration.Abstractions;
using HRManagement.Integration.Abstractions.Insurance;
using HRManagement.Integration.Abstractions.Payroll;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.BankAccounts;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;

namespace Payroll.Infrastructure.Integration;

/// <summary>يبني طلبات التصدير الخارجي من تشغيل رواتب مرحّل/معتمد.</summary>
public sealed class PayrollExternalExportService(
    PayrollDbContext db,
    IEmployeeLookup employeeLookup,
    IPayrollDisbursementGateway disbursementGateway,
    ISocialInsuranceReporter insuranceReporter)
{
    /// <summary>
    /// The insurance shares of the legacy path only (a run pinned to a version older than PLAN-54, or to none): 7% and 14% of gross
    /// pay. A run of a v2 version takes both shares from its version's insurance rules.
    /// </summary>
    private const decimal LegacyEmployeeInsuranceRate = 0.07m;
    private const decimal LegacyEmployerInsuranceRate = 0.14m;

    public async Task<(IntegrationResult Disbursement, IntegrationResult Insurance)> ExportRunAsync(
        Guid payrollRunId,
        CancellationToken cancellationToken = default)
    {
        var run = await db.PayrollRuns
            .Include(r => r.SalaryLines)
            .FirstOrDefaultAsync(r => r.Id == payrollRunId, cancellationToken);
        if (run is null)
            throw new InvalidOperationException("تشغيل الرواتب غير موجود");
        if (run.Status != PayrollRunStatus.Posted)
            throw new InvalidOperationException("يجب ترحيل التشغيل قبل التصدير.");

        var employeeIds = run.SalaryLines.Select(l => l.EmployeeId).Distinct().ToList();
        var names = await employeeLookup.GetEmployeeDisplayNamesAsync(employeeIds, cancellationToken);

        var lineIds = run.SalaryLines.Select(l => l.Id).ToList();
        var components = await db.SalaryLineComponents
            .Where(c => lineIds.Contains(c.SalaryLineId))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var componentsByLine = components.GroupBy(c => c.SalaryLineId).ToDictionary(g => g.Key, g => g.ToList());

        // PLAN-55 (D-PAY-15): the beneficiary account of a row is the account its line pinned at the run's snapshot: an account that
        // changed after the calculation applies from the next cycle. An account is pinned only when it was in force (verified by a
        // second person). One that was retired since is checked below.
        var pinnedAccounts = await BankAccountPinning.PinnedAccountsAsync(db.EmployeeBankAccounts, run.SalaryLines, cancellationToken);

        // PLAN-54 (§2): the insurance rules of the pinned v2 version (the statutory deductions that name an insurance body).
        var pinned = run.PayrollConfigurationId is Guid pinnedId
            ? await db.PayrollConfigurations
                .Include(c => c.StatutoryDeductions)
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == pinnedId, cancellationToken)
            : null;
        var insuranceRules = pinned is { IsLegacyFramework: false }
            ? pinned.StatutoryDeductions.Where(d => !string.IsNullOrEmpty(d.InsuranceBody)).ToList()
            : null;

        var disbursementLines = new List<PayrollDisbursementLine>();
        var insuranceLines = new List<SocialInsuranceContributionLine>();
        var missingAccounts = new List<Guid>();

        foreach (var line in run.SalaryLines.OrderBy(l => l.EmployeeId))
        {
            var displayName = names.GetValueOrDefault(line.EmployeeId);
            var payableIban = line.BankAccountId is Guid accountId && pinnedAccounts.TryGetValue(accountId, out var account)
                ? account.AccountNumber
                : null;

            if (string.IsNullOrWhiteSpace(payableIban))
                missingAccounts.Add(line.EmployeeId);

            disbursementLines.Add(new PayrollDisbursementLine(
                line.EmployeeId,
                // The channel identifies the beneficiary by account; it does not need the national
                // identifier, so the file does not carry one.
                NationalId: null,
                displayName,
                line.GrossPay,
                line.TotalDeductions,
                line.NetPay,
                payableIban));

            var lineComponents = componentsByLine.GetValueOrDefault(line.Id) ?? [];
            decimal employeeInsurance;
            decimal employerInsurance;
            if (insuranceRules is not null)
            {
                // v2: the employee's share is what the run deducted for the version's insurance rules (each component names its
                // rule); the employer's share is each rule's employer percentage of the same capped base. No fixed rate, no name.
                var ruleIds = insuranceRules.Select(r => r.Id).ToHashSet();
                employeeInsurance = lineComponents
                    .Where(c => c.ComponentType == Domain.Enums.SalaryComponentType.Deduction
                                && c.ReferenceId is Guid reference && ruleIds.Contains(reference))
                    .Sum(c => Math.Abs(c.Amount));
                var baseSalary = lineComponents
                    .Where(c => c.ComponentType == Domain.Enums.SalaryComponentType.BaseSalary)
                    .Sum(c => c.Amount);
                employerInsurance = insuranceRules.Sum(r =>
                    pinned!.RoundMoney(r.CappedBase(line.GrossPay, baseSalary) * r.EmployerPercent / 100m));
            }
            else
            {
                // The legacy path, unchanged: the employee's share by the component's name, else 7% of gross; the employer's 14%.
                employeeInsurance = lineComponents
                    .Where(c => c.ComponentType == Domain.Enums.SalaryComponentType.Deduction
                                && (c.ComponentName.Contains("تأمين", StringComparison.OrdinalIgnoreCase)
                                    || c.ComponentName.Contains("Insurance", StringComparison.OrdinalIgnoreCase)))
                    .Sum(c => Math.Abs(c.Amount));

                if (employeeInsurance <= 0)
                    employeeInsurance = decimal.Round(line.GrossPay * LegacyEmployeeInsuranceRate, 2, MidpointRounding.AwayFromZero);

                employerInsurance = decimal.Round(line.GrossPay * LegacyEmployerInsuranceRate, 2, MidpointRounding.AwayFromZero);
            }

            insuranceLines.Add(new SocialInsuranceContributionLine(
                line.EmployeeId,
                NationalId: null,
                displayName,
                line.GrossPay,
                employeeInsurance,
                employerInsurance));
        }

        // PLAN-55 (§1 rule 5): the file is an instruction to pay. A pinned account that was retired since, and whose hold no person
        // released, is not written into it (the disbursement gate refuses the batch for the same reason).
        var retiredAndHeld = await BankAccountPinning.RetiredAndNotReleasedAsync(
            db.EmployeeBankAccounts, db.PaymentHolds, db.PaymentOrders, db.PaymentBatches, run.Id, run.SalaryLines.ToList(), cancellationToken);
        if (retiredAndHeld.Count > 0)
            throw new RetiredPinnedBankAccountsException(retiredAndHeld, names);

        if (missingAccounts.Count > 0)
        {
            // PLAN-55: the refusal names the employees, not their number (a small, temporary change: PLAN-58 replaces this export with a
            // file built from the payment orders). The exception carries their ids; the message, for a screen, their names.
            throw new MissingPinnedBankAccountsException(missingAccounts, names);
        }

        var periodStart = DateOnly.FromDateTime(run.PeriodStart);
        var periodEnd = DateOnly.FromDateTime(run.PeriodEnd);

        var disbursementRequest = new PayrollDisbursementRequest(
            run.Id,
            periodStart,
            periodEnd,
            "SYP",
            disbursementLines);

        var insuranceRequest = new SocialInsuranceReportRequest(
            run.Id,
            periodStart,
            periodEnd,
            insuranceLines);

        var disbursementResult = await disbursementGateway.SubmitAsync(disbursementRequest, cancellationToken);
        var insuranceResult = await insuranceReporter.SubmitAsync(insuranceRequest, cancellationToken);

        PaymentExportMetrics.RecordDisbursement(disbursementResult.Success);
        PaymentExportMetrics.RecordInsurance(insuranceResult.Success);

        return (disbursementResult, insuranceResult);
    }
}
