using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Infrastructure.Data;

namespace Payroll.Infrastructure.Services;

/// <summary>
/// Enforces FRD-001 controls on the disbursement path: an active payment hold or an unresolved
/// critical fraud alert blocks submission of the whole batch.
/// </summary>
public sealed class PaymentDisbursementGate(PayrollDbContext db) : IPaymentDisbursementGate
{
    public async Task<DisbursementGateResult> EvaluateAsync(
        PaymentBatch batch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var heldEmployees = await db.PaymentHolds
            .AsNoTracking()
            .Where(h => h.Status == PaymentHoldStatus.Active
                        && (h.PayrollRunId == null || h.PayrollRunId == batch.PayrollRunId))
            .Select(h => h.EmployeeId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (heldEmployees.Count > 0)
        {
            var affected = await db.SalaryLines
                .AsNoTracking()
                .Where(l => l.PayrollRunId == batch.PayrollRunId && heldEmployees.Contains(l.EmployeeId))
                .CountAsync(cancellationToken);

            if (affected > 0)
            {
                return DisbursementGateResult.Block(
                    $"يوجد {affected} موظف بحجز صرف نشط ضمن هذه الدفعة. يجب الإفراج عن الحجز أو استبعادهم قبل الإرسال.");
            }
        }

        var openCriticalAlerts = await db.FraudAlerts
            .AsNoTracking()
            .CountAsync(
                a => a.PayrollRunId == batch.PayrollRunId
                     && a.Severity == FraudAlertSeverity.Critical
                     && (a.Status == FraudAlertStatus.Open || a.Status == FraudAlertStatus.UnderReview),
                cancellationToken);

        if (openCriticalAlerts > 0)
        {
            return DisbursementGateResult.Block(
                $"يوجد {openCriticalAlerts} تنبيه احتيال حرج غير مغلق لهذا التشغيل.");
        }

        return DisbursementGateResult.Allow();
    }
}
