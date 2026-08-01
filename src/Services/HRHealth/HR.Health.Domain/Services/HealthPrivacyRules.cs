using HR.Health.Domain.Entities;

namespace HR.Health.Domain.Services;

public static class HealthPrivacyRules
{
    public static bool HasActiveConsent(MedicalRecord record, DateTime? utcNow = null)
    {
        if (record.IsCancelled) return false;
        if (record.ConsentWithdrawnAt.HasValue) return false;
        if (!record.ConsentGivenAt.HasValue) return false;
        if (string.IsNullOrWhiteSpace(record.ConsentPurpose)) return false;
        var now = utcNow ?? DateTime.UtcNow;
        if (record.ConsentExpiresAt.HasValue && record.ConsentExpiresAt.Value < now)
            return false;
        return true;
    }

    public static bool CanIssueClinicalAction(MedicalRecord record, bool emergencyOverride) =>
        HasActiveConsent(record) || emergencyOverride;

    public static void SoftCancel(MedicalRecord record, string? reason)
    {
        record.IsCancelled = true;
        record.CancelledAt = DateTime.UtcNow;
        record.CancellationReason = string.IsNullOrWhiteSpace(reason) ? "إلغاء السجل الطبي" : reason.Trim();
    }

    /// <summary>GOV-001 ret-medical = 3650 days — purge only after retention + soft-cancel.</summary>
    public static bool CanPurgeAfterRetention(MedicalRecord record, DateTime? utcNow = null)
    {
        if (!record.IsCancelled || !record.CancelledAt.HasValue) return false;
        var now = utcNow ?? DateTime.UtcNow;
        return record.CancelledAt.Value.AddDays(3650) <= now;
    }
}
