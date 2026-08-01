using HRManagement.Integration.Abstractions;
using HRManagement.Integration.Abstractions.Insurance;
using HRManagement.Integration.Abstractions.Payroll;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
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
    private const decimal DefaultEmployeeInsuranceRate = 0.07m;
    private const decimal DefaultEmployerInsuranceRate = 0.14m;

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

        // Beneficiary accounts for this run. Without them the file is an instruction with no
        // destination, which is why every exported row previously had an empty BankAccount.
        var payProfiles = await db.EmployeePayProfiles
            .AsNoTracking()
            .Where(p => employeeIds.Contains(p.EmployeeId) && p.IsActive)
            .Select(p => new { p.EmployeeId, p.BankAccountIban, p.BankAccountVerifiedAt })
            .ToListAsync(cancellationToken);
        var accountsByEmployee = payProfiles
            .GroupBy(p => p.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First());

        var disbursementLines = new List<PayrollDisbursementLine>();
        var insuranceLines = new List<SocialInsuranceContributionLine>();
        var missingAccounts = new List<Guid>();

        foreach (var line in run.SalaryLines.OrderBy(l => l.EmployeeId))
        {
            var displayName = names.GetValueOrDefault(line.EmployeeId);
            var account = accountsByEmployee.GetValueOrDefault(line.EmployeeId);

            // An unverified account is treated as missing: paying into an account nobody confirmed
            // is how money reaches the wrong person.
            var payableIban = account?.BankAccountVerifiedAt is not null
                ? account.BankAccountIban
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
            var employeeInsurance = lineComponents
                .Where(c => c.ComponentType == Domain.Enums.SalaryComponentType.Deduction
                            && (c.ComponentName.Contains("تأمين", StringComparison.OrdinalIgnoreCase)
                                || c.ComponentName.Contains("Insurance", StringComparison.OrdinalIgnoreCase)))
                .Sum(c => Math.Abs(c.Amount));

            if (employeeInsurance <= 0)
                employeeInsurance = decimal.Round(line.GrossPay * DefaultEmployeeInsuranceRate, 2, MidpointRounding.AwayFromZero);

            var employerInsurance = decimal.Round(line.GrossPay * DefaultEmployerInsuranceRate, 2, MidpointRounding.AwayFromZero);

            insuranceLines.Add(new SocialInsuranceContributionLine(
                line.EmployeeId,
                NationalId: null,
                displayName,
                line.GrossPay,
                employeeInsurance,
                employerInsurance));
        }

        if (missingAccounts.Count > 0)
        {
            throw new InvalidOperationException(
                $"لا يمكن تصدير ملف الصرف: {missingAccounts.Count} موظف بلا حساب مصرفي محقَّق. "
                + "أضف الحساب وتحقّق منه قبل التصدير.");
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
