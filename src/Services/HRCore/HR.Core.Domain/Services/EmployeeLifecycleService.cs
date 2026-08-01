using HR.Core.Domain.Entities;
using HRManagement.SharedKernel.Enums;

namespace HR.Core.Domain.Services;

public static class EmployeeLifecycleService
{
    public static (bool Success, string? Error) TryChangeStatus(
        Employee employee,
        EmploymentStatus newStatus,
        string? reason,
        Guid? changedBy,
        ICollection<EmploymentStatusHistory> historyEntries,
        bool allowAdminOverride = false)
    {
        var oldStatus = employee.CurrentStatus;
        if (oldStatus == newStatus)
            return (true, null);

        if (!allowAdminOverride && !EmploymentStatusRules.CanTransition(oldStatus, newStatus))
            return (false, $"لا يمكن الانتقال من {oldStatus} إلى {newStatus}.");

        if (employee.LegalHold && newStatus == EmploymentStatus.Anonymized)
            return (false, "لا يمكن إخفاء الهوية أثناء الحجز القانوني (Legal Hold).");

        employee.CurrentStatus = newStatus;
        historyEntries.Add(new EmploymentStatusHistory
        {
            EmployeeId = employee.Id,
            OldStatus = oldStatus.ToString(),
            NewStatus = newStatus.ToString(),
            ChangedAt = DateTime.UtcNow,
            ChangedBy = changedBy,
            Reason = reason
        });

        return (true, null);
    }
}
