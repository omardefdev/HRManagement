using HR.Core.Domain.Entities;
using HRManagement.SharedKernel.Enums;

namespace HR.Core.Domain.Services;

/// <summary>DATA-002: legal hold, anonymize, and merge (in-HR.Core bounds).</summary>
public static class EmployeeLegalLifecycleService
{
    public static (bool Success, string? Error) SetLegalHold(
        Employee employee,
        bool hold,
        string? reason,
        Guid actorId)
    {
        if (hold && string.IsNullOrWhiteSpace(reason))
            return (false, "سبب الحجز القانوني مطلوب.");

        employee.LegalHold = hold;
        employee.LegalHoldReason = hold ? reason!.Trim() : null;
        employee.LegalHoldSetAt = hold ? DateTime.UtcNow : null;
        employee.LegalHoldSetBy = hold ? actorId : null;
        return (true, null);
    }

    public static (bool Success, string? Error) TryAnonymize(
        Employee employee,
        ICollection<ContactInfo> contacts,
        Guid actorId,
        ICollection<EmploymentStatusHistory> historyEntries)
    {
        if (employee.LegalHold)
            return (false, "لا يمكن إخفاء الهوية أثناء الحجز القانوني (Legal Hold).");

        var (ok, err) = EmployeeLifecycleService.TryChangeStatus(
            employee,
            EmploymentStatus.Anonymized,
            "AnonymizeEmployee — سياسة معتمدة",
            actorId,
            historyEntries);
        if (!ok) return (false, err);

        var token = employee.Id.ToString("N")[..8].ToUpperInvariant();
        employee.FirstName = "ANON";
        employee.MiddleName = null;
        employee.LastName = token;
        employee.FullName = $"ANON {token}";
        employee.NationalId = $"ANON-{token}";
        employee.PhotoFileId = null;

        foreach (var c in contacts)
            c.Value = $"redacted-{token}@invalid.local";

        return (true, null);
    }

    public static (bool Success, string? Error) TryMerge(
        Employee source,
        Employee target,
        Guid actorId,
        ICollection<EmploymentStatusHistory> historyEntries)
    {
        if (source.Id == target.Id)
            return (false, "لا يمكن دمج الموظف مع نفسه.");

        if (source.LegalHold || target.LegalHold)
            return (false, "لا يمكن الدمج أثناء الحجز القانوني (Legal Hold).");

        if (source.CurrentStatus is EmploymentStatus.Merged or EmploymentStatus.Anonymized)
            return (false, "سجل المصدر مغلق مسبقاً.");

        var (ok, err) = EmployeeLifecycleService.TryChangeStatus(
            source,
            EmploymentStatus.Merged,
            $"MergeEmployees → {target.Id}",
            actorId,
            historyEntries);
        if (!ok) return (false, err);

        source.MergedIntoEmployeeId = target.Id;
        return (true, null);
    }

    /// <summary>GOV-003: reverse a merge when source is Merged, not anonymized, and no legal hold.</summary>
    public static (bool Success, string? Error) TryUnmerge(
        Employee source,
        Guid actorId,
        ICollection<EmploymentStatusHistory> historyEntries,
        EmploymentStatus restoreStatus = EmploymentStatus.Active)
    {
        if (source.CurrentStatus != EmploymentStatus.Merged)
            return (false, "إلغاء الدمج متاح فقط للسجلات بحالة Merged.");

        if (source.LegalHold)
            return (false, "لا يمكن إلغاء الدمج أثناء الحجز القانوني (Legal Hold).");

        if (!source.MergedIntoEmployeeId.HasValue)
            return (false, "لا يوجد هدف دمج مسجّل.");

        var (ok, err) = EmployeeLifecycleService.TryChangeStatus(
            source,
            restoreStatus,
            $"UnmergeEmployees ← {source.MergedIntoEmployeeId}",
            actorId,
            historyEntries,
            allowAdminOverride: true);
        if (!ok) return (false, err);

        source.MergedIntoEmployeeId = null;
        return (true, null);
    }
}
