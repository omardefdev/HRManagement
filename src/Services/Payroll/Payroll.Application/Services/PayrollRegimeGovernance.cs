using HRManagement.SharedKernel.Legal;
using Payroll.Application.Options;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Application.Services;

/// <summary>What HR.Core's service-regime blockers mean for one calculation: refuse it, or hold the blocked employees.</summary>
public sealed record PayrollRegimeDecision(string? Refusal, IReadOnlyList<HrCorePayrollBlockerDto> Blocked)
{
    public static readonly PayrollRegimeDecision Clear = new(null, []);
}

/// <summary>
/// SYR-007: HR.Core reports the employees whose service regime is unclassified or has an open exception. PLAN-58 (D-PAY-4): by default
/// (<c>Payroll:Blockers:Mode=HoldEmployee</c>) everyone is calculated and each blocked employee gets a <c>REGIME-BLOCKER</c> hold, so his
/// order is held and the others are paid; <c>BlockRun</c> (an agency's setting, the behaviour before PLAN-58) refuses the calculation. Both
/// are decided before the run moves to «قيد الحساب»: a refusal leaves the run as it was.
/// </summary>
public static class PayrollRegimeGovernance
{
    public const string ClientMissing = "تعذر التحقق من أنظمة الخدمة — عميل HR.Core غير مهيأ.";

    public static bool RequiresRegimeCheck(PayrollRunType runType) =>
        runType is PayrollRunType.Regular or PayrollRunType.FinalSettlement;

    public static string BlockRunRefusal(int count) =>
        $"لا يمكن إتمام تشغيل الراتب — {count} موظفاً لديهم استثناء نظام خدمة مفتوح أو بدون نظام مصنّف (الإعداد Payroll:Blockers:Mode=BlockRun يمنع الحساب كله).";

    /// <summary>The reason a <c>REGIME-BLOCKER</c> hold carries: HR.Core's own reason code, in a sentence (the employee is shown by name).</summary>
    public static string HoldReason(string? reasonCode) =>
        $"{PaymentHoldType.RegimeBlocker}: حاجز نظام الخدمة لدى HR.Core ({(string.IsNullOrWhiteSpace(reasonCode) ? "دون رمز" : reasonCode.Trim())}): يُحسب راتبه ويُحجز أمره حتى يرفع HR.Core الحاجز ويُفرج عنه بأربع عيون.";

    public static async Task<PayrollRegimeDecision> DecideAsync(
        PayrollRunType runType,
        IReadOnlyList<Guid> employeeIds,
        IHrCoreServiceRegimeClient? hrCoreClient,
        bool blocksFinalPayroll,
        PayrollBlockerMode mode,
        CancellationToken ct = default)
    {
        if (!blocksFinalPayroll || !RequiresRegimeCheck(runType) || employeeIds.Count == 0)
            return PayrollRegimeDecision.Clear;
        if (hrCoreClient is null)
            return new PayrollRegimeDecision(ClientMissing, []);

        var blocked = (await hrCoreClient.GetPayrollBlockersAsync(employeeIds, ct)).Where(b => b.Blocked).ToList();
        if (blocked.Count == 0)
            return PayrollRegimeDecision.Clear;
        return mode == PayrollBlockerMode.BlockRun
            ? new PayrollRegimeDecision(BlockRunRefusal(blocked.Count), blocked)
            : new PayrollRegimeDecision(null, blocked);
    }

    /// <summary>The holds of a decision in <c>HoldEmployee</c>: one per blocked employee and run, not repeated while one stands.</summary>
    public static IReadOnlyList<PaymentHold> HoldsFor(PayrollRun run, PayrollRegimeDecision decision, IReadOnlySet<Guid> alreadyHeld) =>
        decision.Blocked
            .Where(b => !alreadyHeld.Contains(b.EmployeeId))
            .GroupBy(b => b.EmployeeId)
            .Select(g => new PaymentHold
            {
                TenantId = run.TenantId,
                EmployeeId = g.Key,
                PayrollRunId = run.Id,
                HoldType = PaymentHoldType.RegimeBlocker,
                Status = PaymentHoldStatus.Active,
                Reason = HoldReason(g.First().ReasonCode),
                HeldAtUtc = DateTime.UtcNow,
            })
            .ToList();
}
