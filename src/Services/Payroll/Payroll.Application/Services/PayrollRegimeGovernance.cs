using HRManagement.SharedKernel.Legal;
using Payroll.Domain.Enums;

namespace Payroll.Application.Services;

/// <summary>SYR-007: blocks payroll runs when HR.Core reports open service regime exceptions.</summary>
public static class PayrollRegimeGovernance
{
    public static bool RequiresRegimeCheck(PayrollRunType runType) =>
        runType is PayrollRunType.Regular or PayrollRunType.FinalSettlement;

    public static async Task<string?> ValidateEmployeesAsync(
        PayrollRunType runType,
        IReadOnlyList<Guid> employeeIds,
        IHrCoreServiceRegimeClient? hrCoreClient,
        bool blocksFinalPayroll,
        CancellationToken ct = default)
    {
        if (!blocksFinalPayroll || !RequiresRegimeCheck(runType))
            return null;

        if (hrCoreClient is null)
            return "تعذر التحقق من أنظمة الخدمة — عميل HR.Core غير مهيأ.";

        if (employeeIds.Count == 0)
            return null;

        var blockers = await hrCoreClient.GetPayrollBlockersAsync(employeeIds, ct);
        var blocked = blockers.Where(b => b.Blocked).ToList();
        if (blocked.Count == 0)
            return null;

        return $"لا يمكن إتمام تشغيل الراتب — {blocked.Count} موظفاً لديهم استثناء نظام خدمة مفتوح أو بدون نظام مصنّف.";
    }
}
