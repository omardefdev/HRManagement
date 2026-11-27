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

    /// <summary>PLAN-59 (P-6): how many blocked employees the refusal names; the rest are counted («و{n} آخرون»).</summary>
    public const int RefusalNamesShown = 10;

    /// <summary>
    /// PLAN-59 (P-6): the refusal of <c>BlockRun</c> names the blocked employees — the first <see cref="RefusalNamesShown"/> (in the
    /// order of their ids) by display name (<paramref name="displayNames"/>, from <see cref="Abstractions.IEmployeeLookup"/> when the host
    /// has one; else the employee's id), each with HR.Core's reason code, then «و{n} آخرون». A display name and a reason code only: never
    /// a national number.
    /// </summary>
    public static string BlockRunRefusal(
        IReadOnlyList<HrCorePayrollBlockerDto> blocked, IReadOnlyDictionary<Guid, string>? displayNames = null)
    {
        var shown = FirstNamed(blocked);
        var count = blocked.Select(b => b.EmployeeId).Distinct().Count();
        var named = string.Join("، ", shown.Select(b => $"{Label(b.EmployeeId, displayNames)} ({ReasonText(b.ReasonCode)})"));
        var others = count > shown.Count ? $" و{count - shown.Count} آخرون" : string.Empty;
        return $"لا يمكن إتمام تشغيل الراتب — {count} موظفاً لديهم استثناء نظام خدمة مفتوح أو بدون نظام مصنّف: {named}{others} " +
               "(الإعداد Payroll:Blockers:Mode=BlockRun يمنع الحساب كله).";
    }

    /// <summary>The blocked employees a refusal names: one per employee, the first <see cref="RefusalNamesShown"/> by id.</summary>
    public static IReadOnlyList<HrCorePayrollBlockerDto> FirstNamed(IReadOnlyList<HrCorePayrollBlockerDto> blocked) =>
        blocked.GroupBy(b => b.EmployeeId).Select(g => g.First()).OrderBy(b => b.EmployeeId).Take(RefusalNamesShown).ToList();

    private static string Label(Guid employeeId, IReadOnlyDictionary<Guid, string>? displayNames) =>
        displayNames is not null && displayNames.TryGetValue(employeeId, out var name) && !string.IsNullOrWhiteSpace(name)
            && !string.Equals(name.Trim(), Abstractions.EmployeeLookupLabels.Unavailable, StringComparison.Ordinal)
            ? name.Trim()
            : employeeId.ToString("D");

    private static string ReasonText(string? reasonCode) =>
        string.IsNullOrWhiteSpace(reasonCode) ? "دون رمز" : reasonCode.Trim();

    /// <summary>The reason a <c>REGIME-BLOCKER</c> hold carries: HR.Core's own reason code, in a sentence (the employee is shown by name).</summary>
    public static string HoldReason(string? reasonCode) =>
        $"{PaymentHoldType.RegimeBlocker}: حاجز نظام الخدمة لدى HR.Core ({ReasonText(reasonCode)}): يُحسب راتبه ويُحجز أمره حتى يرفع HR.Core الحاجز ويُفرج عنه بأربع عيون.";

    public static async Task<PayrollRegimeDecision> DecideAsync(
        PayrollRunType runType,
        IReadOnlyList<Guid> employeeIds,
        IHrCoreServiceRegimeClient? hrCoreClient,
        bool blocksFinalPayroll,
        PayrollBlockerMode mode,
        CancellationToken ct = default,
        Abstractions.IEmployeeLookup? employeeLookup = null)
    {
        if (!blocksFinalPayroll || !RequiresRegimeCheck(runType) || employeeIds.Count == 0)
            return PayrollRegimeDecision.Clear;
        if (hrCoreClient is null)
            return new PayrollRegimeDecision(ClientMissing, []);

        var blocked = (await hrCoreClient.GetPayrollBlockersAsync(employeeIds, ct)).Where(b => b.Blocked).ToList();
        if (blocked.Count == 0)
            return PayrollRegimeDecision.Clear;
        if (mode != PayrollBlockerMode.BlockRun)
            return new PayrollRegimeDecision(null, blocked);
        // PLAN-59 (P-6): the refusal names the employees, by the names the module shows (a read: the run is not touched).
        var names = employeeLookup is null
            ? null
            : await employeeLookup.GetEmployeeDisplayNamesAsync(FirstNamed(blocked).Select(b => b.EmployeeId), ct);
        return new PayrollRegimeDecision(BlockRunRefusal(blocked, names), blocked);
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
