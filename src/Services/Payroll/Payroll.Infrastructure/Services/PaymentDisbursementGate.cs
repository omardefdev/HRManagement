using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.BankAccounts;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Infrastructure.Data;

namespace Payroll.Infrastructure.Services;

/// <summary>
/// Enforces FRD-001 controls on the disbursement path. PLAN-58 (D-PAY-4, G-PAY-15): per employee. An active payment hold of the batch's
/// run or with no run (a review hold, an activation hold, a service-regime blocker), an open critical alert of the employee, and a pinned
/// account retired with no person's release (PLAN-55) each hold that employee's order only; the other employees are sent. A critical
/// alert that names no employee stops the whole batch, as before.
/// </summary>
public sealed class PaymentDisbursementGate(PayrollDbContext db) : IPaymentDisbursementGate
{
    public const string AlertWithoutEmployee = "يوجد تنبيه احتيال حرج غير مغلق لهذا التشغيل لا يخص موظفاً بعينه؛ لا تُرسل الدفعة حتى يُحسم.";

    public async Task<DisbursementGateResult> EvaluateAsync(PaymentBatch batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var openCritical = await db.FraudAlerts.AsNoTracking()
            .Where(a => a.PayrollRunId == batch.PayrollRunId
                        && a.Severity == FraudAlertSeverity.Critical
                        && (a.Status == FraudAlertStatus.Open || a.Status == FraudAlertStatus.UnderReview))
            .Select(a => new { a.Id, a.EmployeeId })
            .ToListAsync(cancellationToken);
        if (openCritical.Any(a => a.EmployeeId == Guid.Empty))
            return DisbursementGateResult.Stop(AlertWithoutEmployee);

        var lines = await db.SalaryLines.AsNoTracking().Where(l => l.PayrollRunId == batch.PayrollRunId).ToListAsync(cancellationToken);
        var employees = lines.Select(l => l.EmployeeId).ToHashSet();
        var held = new List<HeldEmployee>();

        // A hold of this run, or with no run (it follows the employee into every batch, never the other employees).
        var holds = await db.PaymentHolds.AsNoTracking()
            .Where(h => h.Status == PaymentHoldStatus.Active && (h.PayrollRunId == null || h.PayrollRunId == batch.PayrollRunId))
            .Select(h => new { h.Id, h.EmployeeId, h.HoldType, h.Reason })
            .ToListAsync(cancellationToken);
        foreach (var hold in holds.Where(h => employees.Contains(h.EmployeeId)).OrderBy(h => h.Id))
        {
            var reason = hold.HoldType == PaymentHoldType.RegimeBlocker
                ? PaymentOrderHoldReasons.RegimeBlocker
                : hold.Reason.StartsWith(EmployeeBankAccount.RetiredHoldRule, StringComparison.Ordinal)
                    ? PaymentOrderHoldReasons.AccountRetired
                    : PaymentOrderHoldReasons.PaymentHold;
            held.Add(new HeldEmployee(hold.EmployeeId, reason, hold.Id));
        }

        // PLAN-55 (§1 rule 5): a line whose pinned account was retired and that no person released is not paid, whether or not a hold
        // row exists for it.
        var retired = await BankAccountPinning.RetiredAndNotReleasedAsync(
            db.EmployeeBankAccounts, db.PaymentHolds, db.PaymentOrders, db.PaymentBatches, batch.PayrollRunId, lines, cancellationToken);
        held.AddRange(retired.Select(e => new HeldEmployee(e, PaymentOrderHoldReasons.AccountRetired, null)));

        held.AddRange(openCritical.Where(a => employees.Contains(a.EmployeeId)).OrderBy(a => a.Id)
            .Select(a => new HeldEmployee(a.EmployeeId, PaymentOrderHoldReasons.CriticalAlert, a.Id)));

        return DisbursementGateResult.HoldOnly(held);
    }
}
