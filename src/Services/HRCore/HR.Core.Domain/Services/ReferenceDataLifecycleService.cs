using HRManagement.SharedKernel.MasterData;

namespace HR.Core.Domain.Services;

/// <summary>MDM-001: lifecycle rules for reference catalog entries.</summary>
public static class ReferenceDataLifecycleService
{
    public static (bool Success, string? Error) TryDeactivate(
        ReferenceCatalogEntry entry, string? replacementCode)
    {
        if (!entry.IsActive)
            return (false, "الإدخال غير فعّال بالفعل.");

        entry.IsActive = false;
        entry.ValidTo ??= DateTime.UtcNow;
        entry.ReplacedByCode = replacementCode;
        entry.Version++;

        return (true, null);
    }

    public static (bool Success, string? Error) TryReactivate(ReferenceCatalogEntry entry)
    {
        if (entry.IsActive)
            return (false, "الإدخال فعّال بالفعل.");

        entry.IsActive = true;
        entry.ValidTo = null;
        entry.ReplacedByCode = null;
        entry.Version++;

        return (true, null);
    }

    public static (bool Success, string? Error) ValidateEffectiveDates(ReferenceCatalogEntry entry)
    {
        if (entry.ValidFrom.HasValue && entry.ValidTo.HasValue && entry.ValidFrom > entry.ValidTo)
            return (false, "تاريخ البداية يجب أن يسبق تاريخ النهاية.");

        return (true, null);
    }

    public static bool IsEffectiveAt(ReferenceCatalogEntry entry, DateTime asOf)
    {
        if (!entry.IsActive) return false;
        if (entry.ValidFrom.HasValue && asOf < entry.ValidFrom.Value) return false;
        if (entry.ValidTo.HasValue && asOf > entry.ValidTo.Value) return false;
        return true;
    }
}
