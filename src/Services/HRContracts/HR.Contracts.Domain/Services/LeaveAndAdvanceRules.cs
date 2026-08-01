using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HRManagement.SharedKernel.Enums;

namespace HR.Contracts.Domain.Services;

public static class LeaveAndAdvanceRules
{
    public const decimal DefaultMaxSingleAdvance = 100_000m;
    public const int DefaultMaxInstallments = 12;
    public const decimal DefaultMaxAdvanceToSalaryRatio = 3m;

    public static int ComputeInclusiveDays(DateOnly start, DateOnly end) =>
        end.DayNumber - start.DayNumber + 1;

    /// <summary>Leave duration for balance and payroll uses working days (Fri/Sat excluded).</summary>
    public static int ComputeLeaveDurationDays(DateOnly start, DateOnly end) =>
        CalculateWorkingDays(start, end);

    public static bool OverlapsApprovedLeave(
        IEnumerable<LeaveRequest> existing,
        Guid employeeId,
        DateOnly start,
        DateOnly end,
        Guid? excludeLeaveId)
    {
        foreach (var o in existing)
        {
            if (o.EmployeeId != employeeId || o.Status != ApprovalStatus.Approved)
                continue;
            if (excludeLeaveId.HasValue && o.Id == excludeLeaveId.Value)
                continue;
            if (start <= o.EndDate && end >= o.StartDate)
                return true;
        }

        return false;
    }

    public static string? ValidateLeaveRequest(
        Guid employeeId,
        DateOnly start,
        DateOnly end,
        int durationDays,
        IEnumerable<LeaveRequest> existing,
        Guid? excludeLeaveId)
    {
        if (end < start)
            return "تاريخ نهاية الإجازة يجب ألا يكون قبل تاريخ البداية.";

        var expected = ComputeLeaveDurationDays(start, end);
        if (expected <= 0)
            return "يجب أن تحتوي فترة الإجازة على يوم عمل واحد على الأقل.";
        if (durationDays != expected)
            return $"عدد أيام العمل ({durationDays}) لا يطابق الفترة من {start} إلى {end} (المتوقع {expected} يوم عمل).";

        if (OverlapsApprovedLeave(existing, employeeId, start, end, excludeLeaveId))
            return "توجد إجازة معتمدة متداخلة مع هذه الفترة.";

        return null;
    }

    public static string? ValidateLeaveBalance(
        LeaveBalance? balance,
        int requestedDays)
    {
        if (balance is null)
            return "لا يوجد رصيد إجازات مسجل لهذا الموظف/النوع/السنة.";

        if (balance.Remaining < requestedDays)
            return $"الرصيد المتبقي ({balance.Remaining} يوم) غير كافٍ للمدة المطلوبة ({requestedDays} يوم).";

        return null;
    }

    public static string? ValidateLeavePolicy(
        LeavePolicy? policy,
        LeaveType leaveType,
        DateOnly requestDate,
        DateOnly startDate)
    {
        if (policy is null)
            return null;

        if (policy.MinNoticeDays > 0)
        {
            var noticeDays = startDate.DayNumber - requestDate.DayNumber;
            if (noticeDays < policy.MinNoticeDays)
                return $"يجب تقديم طلب الإجازة قبل {policy.MinNoticeDays} يوم على الأقل من تاريخ البداية.";
        }

        return null;
    }

    public static string? ValidateAdvanceRequest(
        decimal amount,
        decimal maxSingleAdvance,
        IEnumerable<AdvanceRequest> employeeAdvances,
        Guid? excludeAdvanceId)
    {
        if (amount <= 0)
            return "مبلغ السلفة يجب أن يكون أكبر من صفر.";

        if (amount > maxSingleAdvance)
            return $"مبلغ السلفة يتجاوز الحد الأقصى المسموح ({maxSingleAdvance}).";

        foreach (var a in employeeAdvances)
        {
            if (excludeAdvanceId.HasValue && a.Id == excludeAdvanceId.Value)
                continue;
            if (a.Status == ApprovalStatus.Pending)
                return "لا يمكن تقديم سلفة جديدة طالما هناك طلب قيد الانتظار لنفس الموظف.";
        }

        return null;
    }

    public static string? ValidateAdvanceAgainstSalary(
        decimal amount,
        decimal monthlySalary,
        decimal maxRatio = 3m)
    {
        if (monthlySalary <= 0)
            return null;

        var maxAllowed = monthlySalary * maxRatio;
        if (amount > maxAllowed)
            return $"مبلغ السلفة ({amount}) يتجاوز الحد الأقصى ({maxRatio} أضعاف الراتب = {maxAllowed}).";

        return null;
    }

    public static string? ValidateAdvanceInstallmentRatio(
        decimal amount,
        int numberOfInstallments,
        decimal monthlySalary,
        decimal maxRatio = 0.30m)
    {
        if (numberOfInstallments <= 0 || monthlySalary <= 0)
            return null;

        var monthlyInstallment = Math.Round(amount / numberOfInstallments, 2);
        var maxAllowed = monthlySalary * maxRatio;
        if (monthlyInstallment > maxAllowed)
            return $"القسط الشهري ({monthlyInstallment}) يتجاوز {maxRatio * 100:0}% من الراتب ({maxAllowed}).";

        return null;
    }

    public static string? ValidateNoOpenAdvance(
        IEnumerable<AdvanceRequest> employeeAdvances,
        IEnumerable<AdvanceInstallmentPlan> plans,
        Guid? excludeAdvanceId)
    {
        foreach (var a in employeeAdvances)
        {
            if (excludeAdvanceId.HasValue && a.Id == excludeAdvanceId.Value) continue;
            if (a.Status != ApprovalStatus.Approved) continue;

            var plan = plans.FirstOrDefault(p => p.AdvanceRequestId == a.Id);
            if (plan is not null && plan.RemainingAmount > 0)
                return "لا يجوز طلب سلفة جديدة أثناء وجود سلفة قائمة لم تُسدد بالكامل.";
        }
        return null;
    }

    public static int CalculateWorkingDays(DateOnly start, DateOnly end)
    {
        if (end < start) return 0;
        int count = 0;
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Friday or DayOfWeek.Saturday))
                count++;
        }
        return count;
    }

    public static int GetMaxDuration(LeaveType leaveType) => leaveType switch
    {
        LeaveType.Annual => 30,
        LeaveType.Sick => 15,
        LeaveType.Maternity => 90,
        LeaveType.Unpaid => 60,
        _ => 30,
    };

    public static string? ValidateMaxDuration(LeaveType leaveType, int durationDays)
    {
        var max = GetMaxDuration(leaveType);
        if (durationDays > max)
            return $"مدة الإجازة ({durationDays} يوم) تتجاوز الحد الأقصى لنوع {leaveType} ({max} يوم).";
        return null;
    }

    public static decimal GetInstallmentAmount(decimal totalAmount, int numberOfInstallments, int installmentIndex)
    {
        if (numberOfInstallments <= 0) numberOfInstallments = 1;
        if (installmentIndex < 0) installmentIndex = 0;
        if (installmentIndex >= numberOfInstallments) installmentIndex = numberOfInstallments - 1;

        var monthly = Math.Round(totalAmount / numberOfInstallments, 2, MidpointRounding.AwayFromZero);
        return installmentIndex == numberOfInstallments - 1
            ? totalAmount - (monthly * (numberOfInstallments - 1))
            : monthly;
    }

    public static AdvanceInstallmentPlan CreateInstallmentPlan(
        Guid advanceRequestId,
        decimal totalAmount,
        int numberOfInstallments = 0)
    {
        if (numberOfInstallments <= 0)
            numberOfInstallments = Math.Min(DefaultMaxInstallments, (int)Math.Ceiling(totalAmount / 5000m));
        numberOfInstallments = Math.Max(1, numberOfInstallments);

        var monthly = GetInstallmentAmount(totalAmount, numberOfInstallments, 0);

        return new AdvanceInstallmentPlan
        {
            AdvanceRequestId = advanceRequestId,
            TotalAmount = totalAmount,
            MonthlyInstallment = monthly,
            NumberOfInstallments = numberOfInstallments,
            StartMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(1),
            RemainingAmount = totalAmount
        };
    }

    public static decimal CalculateInstallments(decimal amount, int numberOfInstallments)
    {
        if (numberOfInstallments <= 0) numberOfInstallments = 1;
        return GetInstallmentAmount(amount, numberOfInstallments, 0);
    }
}
